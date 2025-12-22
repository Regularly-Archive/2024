using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.Common.Models.KernelMemory;
using PostgreSQL.Embedding.Common.Models.Planners;
using PostgreSQL.Embedding.Common.Models.RAG;
using PostgreSQL.Embedding.Common.Models.Search;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.LLmServices.Extensions;
using PostgreSQL.Embedding.Planners;
using PostgreSQL.Embedding.Plugins;
using PostgreSQL.Embedding.Utils;
using SqlSugar;
using System.Text;
using System.Text.RegularExpressions;

namespace PostgreSQL.Embedding.LlmServices
{
    public class RAGFlowService : BaseConversationService, IRAGFlowService
    {
        private readonly Kernel _kernel;
        private readonly IServiceProvider _serviceProvider;
        private readonly IRepository<LlmAppKnowledge> _llmAppKnowledgeRepository;
        private readonly IRepository<KnowledgeBase> _knowledgeBaseRepository;
        private readonly IRepository<LlmApp> _llmAppRepository;
        private readonly IMemoryService _memoryService;
        private readonly IChatHistoriesService _chatHistoriesService;
        private readonly PromptTemplateService _promptTemplateService;
        private readonly ILogger<RAGFlowService> _logger;
        private readonly CallablePromptTemplate _promptTemplate;
        private readonly AgentExecutionContext _agentExecutionContext;
        private readonly Regex _regexCitations = new Regex(@"\^(\d+)", RegexOptions.Compiled);
        private SSEEmitter _sseEmitter;
        public RAGFlowService(Kernel kernel,
            IServiceProvider serviceProvider,
            IMemoryService memoryService,
            IChatHistoriesService chatHistoriesService
        ) : base(kernel, chatHistoriesService)
        {
            _kernel = kernel;
            _serviceProvider = serviceProvider;
            _memoryService = memoryService;
            _chatHistoriesService = chatHistoriesService;
            _llmAppRepository = serviceProvider.GetService<IRepository<LlmApp>>();
            _knowledgeBaseRepository = serviceProvider.GetService<IRepository<KnowledgeBase>>();
            _llmAppKnowledgeRepository = serviceProvider.GetService<IRepository<LlmAppKnowledge>>();
            _promptTemplateService = serviceProvider.GetService<PromptTemplateService>();
            _promptTemplate = _promptTemplateService.LoadTemplate("RAGPrompt.txt");
            _promptTemplate.FunctionName = "RAGFlowService_GenerateAnswer";
            _logger = _serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<RAGFlowService>();
            var httpContext = _serviceProvider.GetRequiredService<IHttpContextAccessor>()?.HttpContext;
            _sseEmitter = new SSEEmitter(httpContext);
            _agentExecutionContext = _kernel.GetAgentExecutionContext();
        }

        /// <summary>
        /// 生成答案
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="conversationId"></param>
        /// <param name="input"></param>
        /// <param name="citations"></param>
        /// <returns></returns>
        public async Task<string> GenerateAnswerAsync(long appId, string conversationId, string input, List<LlmCitationModel> citations)
        {
            if (citations == null || !citations.Any())
            {
                return Common.Constants.DefaultEmptyAnswer;
            }

            var app = await _llmAppRepository.GetAsync(appId);

            var context = JsonConvert.SerializeObject(citations);

            var temperature = app.Temperature / 100;
            var executionSettings = new OpenAIPromptExecutionSettings() { Temperature = (double)temperature };

            var histories = await GetHistoricalMessagesAsync(app.Id, conversationId, app.MaxMessageRounds);

            _promptTemplate.AddVariable("name", app.Name ?? "ChatGPT");
            _promptTemplate.AddVariable("context", context);
            _promptTemplate.AddVariable("question", input);
            _promptTemplate.AddVariable("empty_answer", Common.Constants.DefaultEmptyAnswer);
            _promptTemplate.AddVariable("histories", histories);

            var answer = string.Empty;
            await foreach (var content in _promptTemplate.InvokeStreamingAsync(_kernel, executionSettings))
            {
                answer += content.Content;
            }

            if (!string.IsNullOrEmpty(answer))
            {
                if (answer.IndexOf(Common.Constants.DefaultEmptyAnswer) != -1)
                    return $"<KEEP_FORMAT>{Common.Constants.DefaultEmptyAnswer}</KEEP_FORMAT>";


                // 匹配引用信息，对引用信息的索引进行重排
                var newAnswer = ReorderReferences(citations, answer);
                return $"<KEEP_FORMAT>{newAnswer}</KEEP_FORMAT>";
            }

            return Common.Constants.DefaultEmptyAnswer;
        }

        /// <summary>
        /// 生成答案（适用于非应用场景下）
        /// </summary>
        /// <param name="input"></param>
        /// <param name="citations"></param>
        /// <returns></returns>
        public async Task<string> GenerateAnswerAsync(string input, List<LlmCitationModel> citations)
        {
            var context = JsonConvert.SerializeObject(citations);

            _promptTemplate.AddVariable("name", "ChatGPT");
            _promptTemplate.AddVariable("context", context);
            _promptTemplate.AddVariable("question", input);
            _promptTemplate.AddVariable("empty_answer", Common.Constants.DefaultEmptyAnswer);
            _promptTemplate.AddVariable("histories", string.Empty);

            var answer = string.Empty;
            await foreach (var content in _promptTemplate.InvokeStreamingAsync(_kernel))
            {
                answer += content.Content;
            }

            if (!string.IsNullOrEmpty(answer))
            {
                if (answer.IndexOf(Common.Constants.DefaultEmptyAnswer) != -1)
                    return $"<KEEP_FORMAT>{Common.Constants.DefaultEmptyAnswer}</KEEP_FORMAT>";


                // 匹配引用信息，对引用信息的索引进行重排
                var newAnswer = ReorderReferences(citations, answer);
                return $"<KEEP_FORMAT>{newAnswer}</KEEP_FORMAT>";
            }

            return Common.Constants.DefaultEmptyAnswer;
        }

        /// <summary>
        /// 生成引用
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="question"></param>
        /// <returns></returns>
        public async Task<List<LlmCitationModel>> GenerateCitationsAsync(long appId, string question, bool enableWebSearch = false)
        {
            var app = await _llmAppRepository.GetAsync(appId);
            if (app == null) return [];

            var llmCitations = new List<LlmCitationModel>();
            var inputs = new List<string> { question };

            if (app.EnableRewrite)
            {
                var similarQuestions = await RewriteQueryAsync(question, app.RewriteQueryCount, _kernel);

                await EmitStreamingTracesAsync(StepTrace.Thought(question, $" 开始重写查询，共生成 {similarQuestions.Count} 个相似问题：{JsonConvert.SerializeObject(similarQuestions)}。", _agentExecutionContext.GetStepId(), _agentExecutionContext.GetMessageId()));
                _logger.LogInformation($" 开始重写查询，共生成 {similarQuestions.Count} 个相似问题：{JsonConvert.SerializeObject(similarQuestions)}。");

                if (similarQuestions.Any()) { inputs.AddRange(similarQuestions); }
            }

            var docCitations = await GenerateCitationsByDocuments(app, question, inputs, 10);
            llmCitations.AddRange(docCitations);

            if (enableWebSearch)
            {
                var webCitations = await GenerateCitationsByWebSearch(app, question, inputs, 10);
                llmCitations.AddRange(webCitations);
            }


            llmCitations.OrderByDescending(x => x.Relevance).ForEach((item, index) => item.Index = index + 1);
            return llmCitations;
        }

        /// <summary>
        /// 检索文档
        /// </summary>
        /// <param name="knowledgeBase"></param>
        /// <param name="question"></param>
        /// <returns></returns>
        private async Task<List<KMCitation>> RetrieveDocumentsAsync(KnowledgeBase knowledgeBase, string question, int limit, double minRelevance)
        {
            using var serviceScope = _serviceProvider.CreateScope();
            var knowledgeBaseService = _memoryService.AsKnowledgeBaseService(serviceScope.ServiceProvider);
            if (knowledgeBase.RetrievalType == (int)RetrievalType.Vectors)
            {
                // 向量检索
                var searchResult = await knowledgeBaseService.SearchAsync(knowledgeBase.Id, question, RetrievalType.Vectors, minRelevance, limit);
                return searchResult.RelevantSources;

            }
            else if (knowledgeBase.RetrievalType == (int)RetrievalType.FullText)
            {
                // 全文检索
                var searchResult = await knowledgeBaseService.SearchAsync(knowledgeBase.Id, question, RetrievalType.FullText, minRelevance, limit);
                return searchResult.RelevantSources;
            }
            else
            {
                // 混合检索
                var searchResult = await knowledgeBaseService.SearchAsync(knowledgeBase.Id, question, RetrievalType.Mixed, minRelevance, limit);
                return searchResult.RelevantSources;
            }
        }

        /// <summary>
        /// 查询重写
        /// </summary>
        /// <param name="question"></param>
        /// <param name="kernel"></param>
        /// <returns></returns>
        public async Task<List<string>> RewriteQueryAsync(string question, int limit, Kernel kernel)
        {

            try
            {
                var rewritePromptTemplate = _promptTemplateService.LoadTemplate("RewritePrompt.txt");
                rewritePromptTemplate.FunctionName = "RAGFlowService_RewriteQuery";
                var executionSettings = new OpenAIPromptExecutionSettings() { Temperature = 0f };

                rewritePromptTemplate.AddVariable("question", question);
                rewritePromptTemplate.AddVariable("limit", limit);
                var invokeResult = await rewritePromptTemplate.InvokeAsync(kernel, executionSettings);

                var payload = invokeResult.GetValue<string>();
                if (string.IsNullOrEmpty(payload)) return [];

                payload = payload.Replace("```json", "").Replace("```", "");
                var llmRewriteResult = JsonConvert.DeserializeObject<LlmRewriteResult>(payload);
                return llmRewriteResult.Output;
            }
            catch (Exception ex)
            {
                // Todo
                _logger.LogError(ex, "The rewrite flow has been stoped due to unexpected reason: {0}", ex.Message);
                return [];
            }
        }

        /// <summary>
        /// 文档重排
        /// </summary>
        /// <param name="question"></param>
        /// <param name="partitions"></param>
        /// <returns></returns>
        public List<KMPartition> RerankDocuments(string question, List<KMPartition> partitions, RerankerType rerankerType = RerankerType.BM25)
        {
            using var serviceScope = _serviceProvider.CreateScope();
            var rerankService = serviceScope.ServiceProvider.GetKeyedService<IRerankService>(rerankerType.ToString());
            if (!partitions.Any()) return partitions;

            try
            {
                var rerankResult = rerankService.Sort(question, partitions, x => x.Text).ToList();
                foreach (var item in rerankResult)
                {
                    var score = item.Score;
                    item.Document.SetRelevance((float)score);
                }

                return rerankResult.Select(x => x.Document).ToList();
            }
            catch (Exception ex)
            {
                // Todo
                _logger.LogError(ex, "");
                return partitions;
            }
        }

        /// <summary>
        /// 网页重排
        /// </summary>
        /// <param name="question"></param>
        /// <param name="entries"></param>
        /// <param name="rerankerType"></param>
        /// <returns></returns>
        private List<Entry> RerankEntries(string question, List<Entry> entries, RerankerType rerankerType = RerankerType.BM25)
        {
            using var serviceScope = _serviceProvider.CreateScope();
            var rerankService = serviceScope.ServiceProvider.GetKeyedService<IRerankService>(rerankerType.ToString());
            if (!entries.Any()) return entries;

            try
            {
                var rerankResult = rerankService.Sort(question, entries, x => $"{x.Title} {x.Snippet}").ToList();
                foreach (var item in rerankResult)
                {
                    var score = item.Score;
                    item.Document.Relevance = (float)score;
                }

                return rerankResult.Select(x => x.Document).ToList();
            }
            catch (Exception ex)
            {
                // Todo
                _logger.LogError(ex, "");
                return entries;
            }
        }

        /// <summary>
        /// 从文档中生成引用信息
        /// </summary>
        /// <param name="app"></param>
        /// <param name="question"></param>
        /// <param name="inputs"></param>
        /// <param name="topN"></param>
        /// <returns></returns>
        private async Task<List<LlmCitationModel>> GenerateCitationsByDocuments(LlmApp app, string question, List<string> inputs, int topN = 10)
        {
            var searchResults = new List<KMCitation>();

            var llmKappKnowledges = await _llmAppKnowledgeRepository.FindListAsync(x => x.AppId == app.Id);
            if (!llmKappKnowledges.Any()) return [];

            foreach (var appKnowledge in llmKappKnowledges)
            {
                var knowledgeBase = await _knowledgeBaseRepository.GetAsync(appKnowledge.KnowledgeBaseId);
                if (knowledgeBase == null) continue;

                var limit = knowledgeBase.RetrievalLimit.HasValue ?
                    knowledgeBase.RetrievalLimit.Value : PostgreSQL.Embedding.Common.Constants.DefaultRetrievalLimit;

                var minRelevance = knowledgeBase.RetrievalRelevance.HasValue ?
                    knowledgeBase.RetrievalRelevance.Value / 100 : PostgreSQL.Embedding.Common.Constants.DefaultRetrievalRelevance;

                foreach (var input in inputs)
                {
                    var retrieveResult = await RetrieveDocumentsAsync(knowledgeBase, input, limit, (double)minRelevance);
                    if (!retrieveResult.Any()) continue;

                    searchResults.AddRange(retrieveResult);
                }
            }

            var partitions = searchResults.SelectMany(x => x.Partitions).ToList();
            partitions = partitions.DistinctBy(x => new { x.KnowledgeBaseId, x.FileId, x.PartId }).ToList();

            if (app.EnableRerank) partitions = RerankDocuments(question, partitions, (RerankerType)app.RerankerType);

            var citations = partitions.Select((x, i) => LlmCitationModel.FromKnowledgeBase(i + 1, x))
                .OrderByDescending(x => x.Relevance)
                .Take(topN)
                .ToList();

            if (citations.Any())
            {
                await EmitStreamingTracesAsync(StepTrace.Thought(question, $"共检索到 {partitions.Count} 个文档块，即将生成答案。", _agentExecutionContext.GetStepId(), _agentExecutionContext.GetMessageId()));
                _logger.LogInformation($"已检索到 {partitions.Count} 个文档块，即将生成答案。");
            }

            return citations;
        }

        /// <summary>
        /// 从网络搜索中生成引用信息
        /// </summary>
        /// <param name="app"></param>
        /// <param name="question"></param>
        /// <param name="inputs"></param>
        /// <param name="topN"></param>
        /// <returns></returns>
        private async Task<List<LlmCitationModel>> GenerateCitationsByWebSearch(LlmApp app, string question, List<string> inputs, int topN = 10)
        {
            var webSearchPlugin = _serviceProvider.GetRequiredService<WebSearchPlugin>();
            var webSearchResults = new List<SearchResult>();

            foreach (var input in inputs)
            {
                var searchResult = await webSearchPlugin.SearchAsync(input);
                if (!searchResult.Entries.Any()) continue;

                webSearchResults.Add(searchResult);
            }

            var searchEntries = webSearchResults.SelectMany(x => x.Entries).ToList();
            if (app.EnableRerank) searchEntries = RerankEntries(question, searchEntries, (RerankerType)app.RerankerType);

            var citatios = searchEntries
                .DistinctBy(x => x.Url)
                .OrderByDescending(x => x.Relevance)
                .Take(topN)
                .Select((x, i) => LlmCitationModel.FromSearchEngine(i + 1, x))
                .ToList();

            if (citatios.Any())
            {
                await EmitStreamingTracesAsync(StepTrace.Thought(question, $"已阅读 {searchEntries.Count} 个网页，即将生成答案。", _agentExecutionContext.GetStepId(), _agentExecutionContext.GetMessageId()));
                _logger.LogInformation($"已阅读 {searchEntries.Count} 个网页，即将生成答案。");
            }

            return citatios;
        }


        /// <summary>
        /// 重新为引用项编号
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        private string ReorderReferences(List<LlmCitationModel> originCitations, string generatedAnswer)
        {
            var markdownCitations = new HashSet<string>();

            var matches = _regexCitations.Matches(generatedAnswer);

            var referenceOrder = new List<int>();
            var usedReferences = new HashSet<int>();

            foreach (Match match in matches)
            {
                var referenceNumber = int.Parse(match.Groups[1].Value);
                usedReferences.Add(referenceNumber);

                if (!referenceOrder.Contains(referenceNumber))
                    referenceOrder.Add(referenceNumber);
            }

            var referenceMapping = new Dictionary<int, int>();
            for (int i = 0; i < referenceOrder.Count; i++)
            {
                referenceMapping[referenceOrder[i]] = i + 1;
            }

            string result = _regexCitations.Replace(generatedAnswer, match =>
            {
                var oldNumber = int.Parse(match.Groups[1].Value);
                var newNumber = referenceMapping[oldNumber];
                var citation = originCitations.First(x => x.Index == oldNumber);
                markdownCitations.Add($"[{newNumber}]: {citation.Url}");
                return $"<sup>[{newNumber}]</sup>";
            });

            result = result.Replace("^", "")
                .Replace("（<sup>", "<sup>")
                .Replace("(<sup>", "<sup>")
                .Replace("</sup>）", "</sup>")
                .Replace("</sup>)", "</sup>");

            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(result);
            stringBuilder.AppendLine();
            stringBuilder.AppendLine($"<CITATIONS>{string.Join("\r\n", markdownCitations)}</CITATIONS>");

            return stringBuilder.ToString();
        }

        private async Task EmitTracesAsync(StepTrace stepTrace, CancellationToken cancellationToken = default)
        {
            var result = new OpenAIStreamResult() { id = Guid.NewGuid().ToString("N"), obj = "chat.traces" };
            result.choices.Add(new StreamChoicesModel() { delta = new OpenAIMessage() { role = "assistant", content = JsonConvert.SerializeObject(stepTrace) } });
            await _sseEmitter.EmitAsync(result, cancellationToken);
        }

        private async Task EmitStreamingTracesAsync(StepTrace stepTrace, CancellationToken cancellationToken = default)
        {
            if (stepTrace.Type != "Thought") return;

            var streamingStepTraces = stepTrace.AsStreamingThought();
            foreach (var streamingStepTrace in streamingStepTraces)
            {
                await Task.Delay(100);
                await EmitTracesAsync(streamingStepTrace, cancellationToken);
            }

        }
    }
}
