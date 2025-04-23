using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.Common.Models.KernelMemory;
using PostgreSQL.Embedding.Common.Models.RAG;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.LLmServices.Extensions;
using SqlSugar;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

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
        private readonly ILogger<RAGConversationService> _logger;
        private readonly CallablePromptTemplate _promptTemplate;
        private Regex _regexCitations = new Regex(@"\[(\d+)\]");
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
            _logger = _serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<RAGConversationService>();
        }

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
            var chatResult = await _promptTemplate.InvokeAsync(_kernel, executionSettings);

            var answer = chatResult.GetValue<string>();
            if (!string.IsNullOrEmpty(answer))
            {
                if (answer.IndexOf(Common.Constants.DefaultEmptyAnswer) != -1)
                {
                    return $"<RAG>{Common.Constants.DefaultEmptyAnswer}</RAG>";
                }
                else
                {
                    // 匹配引用信息，对引用信息的索引进行重排
                    var index = 0;
                    var matchedCitationNumbers = _regexCitations.Matches(answer).Select(x => int.Parse(x.Groups[1].Value)).ToList();
                    var newCitationNumbers = new List<LlmCitationMappingModel>();
                    foreach (var citationNumber in matchedCitationNumbers)
                    {
                        if (!newCitationNumbers.Any(x => x.OriginIndex == citationNumber))
                        {
                            index += 1;
                            newCitationNumbers.Add(new LlmCitationMappingModel() { NewIndex = index, OriginIndex = citationNumber });
                        }
                    }

                    // 重新生成引用信息
                    var generatedCitations = citations.Where(x => matchedCitationNumbers.Contains(x.Index)).Select(x =>
                    {
                        var newIndex = newCitationNumbers.FirstOrDefault(k => k.OriginIndex == x.Index).NewIndex;
                        return new LlmCitationModel() { Index = newIndex, Url = x.Url };
                    })
                    .OrderBy(x => x.Index)
                    .Select(x => $"[{x.Index}]: {x.Url}")
                    .ToList();

                    var markdownFormatContext = string.Join("\r\n", generatedCitations);

                    // 更新答案中的引用信息
                    foreach (var ciation in newCitationNumbers)
                    {
                        answer = answer.Replace($"[{ciation.OriginIndex}]", $"[{ciation.NewIndex}]");
                    }

                    // 拼接答案和引用信息
                    var answerBuilder = new StringBuilder();
                    answerBuilder.AppendLine(answer);
                    answerBuilder.AppendLine();
                    answerBuilder.AppendLine(markdownFormatContext);

                    return $"<RAG>{answerBuilder.ToString()}</RAG>";
                }
            }

            return Common.Constants.DefaultEmptyAnswer;
        }

        public async Task<List<LlmCitationModel>> RetrieveCitationsAsync(long appId, string question)
        {
            var app = await _llmAppRepository.GetAsync(appId);
            if (app == null) return [];


            var llmKappKnowledges = await _llmAppKnowledgeRepository.FindListAsync(x => x.AppId == app.Id);
            if (!llmKappKnowledges.Any()) return [];

            var searchResults = new List<KMCitation>();
            var inputs = new List<string> { question };

            // 查询重写
            if (app.EnableRewrite)
            {
                var similarQuestions = await RewriteAsync(question, _kernel);
                _logger.LogInformation($"查询重写，共生成 {similarQuestions.Count} 个相似问题：{JsonConvert.SerializeObject(similarQuestions)}.");
                //await EmitTracesAsync($"查询重写，共生成 {similarQuestions.Count} 个相似问题", cancellationToken);

                //similarQuestions.ForEach(async similarQuestion => await EmitTracesAsync(similarQuestion, cancellationToken));
                if (similarQuestions.Any()) { inputs.AddRange(similarQuestions); }
            }

            foreach (var appKnowledge in llmKappKnowledges)
            {
                var knowledgeBase = await _knowledgeBaseRepository.GetAsync(appKnowledge.KnowledgeBaseId);
                if (knowledgeBase == null) continue;

                foreach (var input in inputs)
                {
                    var retrieveResult = await RetrieveAsync(knowledgeBase, input);
                    if (retrieveResult != null && retrieveResult.Any()) searchResults.AddRange(retrieveResult);
                }
            }

            var partitions = searchResults.SelectMany(x => x.Partitions).ToList();

            // 结果重排
            if (app.EnableRerank) partitions = Rerank(question, partitions);

            // 构建上下文
            var chunks = partitions.Select((x, i) => LlmCitationModel.FromKnowledgeBase(i + 1, x))
            .OrderByDescending(x => x.Relevance)
            .Take(10)
            .ToList();

            if (chunks.Any())
            {
                var maxRelevance = chunks.Max(x => x.Relevance);
                var minRelevance = chunks.Min(x => x.Relevance);

                //_logger.LogInformation($"共检索到 {chunks.Count} 个文档块，相似度区间为 {minRelevance} ~ {maxRelevance}");
                //await EmitTracesAsync($"共检索到 {chunks.Count} 个文档块，相似度区间为 {minRelevance} ~ {maxRelevance}", cancellationToken);
            }
            else
            {
                _logger.LogInformation($"未检索到符合条件的文档块");
                //await EmitTracesAsync($"未检索到符合条件的文档块", cancellationToken);
            }

            return chunks;
        }

        private async Task<List<KMCitation>> RetrieveAsync(KnowledgeBase knowledgeBase, string question)
        {
            var limit = knowledgeBase.RetrievalLimit.HasValue ?
                knowledgeBase.RetrievalLimit.Value : PostgreSQL.Embedding.Common.Constants.DefaultRetrievalLimit;

            var minRelevance = knowledgeBase.RetrievalRelevance.HasValue
                ? knowledgeBase.RetrievalRelevance.Value / 100 : PostgreSQL.Embedding.Common.Constants.DefaultRetrievalRelevance;

            using var serviceScope = _serviceProvider.CreateScope();
            var knowledgeBaseService = _memoryService.AsKnowledgeBaseService(serviceScope.ServiceProvider);
            if (knowledgeBase.RetrievalType == (int)RetrievalType.Vectors)
            {
                // 向量检索
                var searchResult = await knowledgeBaseService.SearchAsync(knowledgeBase.Id, question, RetrievalType.Vectors, (double)minRelevance, limit);
                return searchResult.RelevantSources;

            }
            else if (knowledgeBase.RetrievalType == (int)RetrievalType.FullText)
            {
                // 全文检索
                var searchResult = await knowledgeBaseService.SearchAsync(knowledgeBase.Id, question, RetrievalType.FullText, (double)minRelevance, limit);
                return searchResult.RelevantSources;
            }
            else
            {
                // 混合检索
                var searchResult = await knowledgeBaseService.SearchAsync(knowledgeBase.Id, question, RetrievalType.Mixed, (double)minRelevance, limit);
                return searchResult.RelevantSources;
            }
        }

        private async Task<List<string>> RewriteAsync(string question, Kernel kernel)
        {

            try
            {
                var rewritePromptTemplate = _promptTemplateService.LoadTemplate("RewritePrompt.txt");
                var executionSettings = new OpenAIPromptExecutionSettings() { Temperature = 0f };

                rewritePromptTemplate.AddVariable("question", question);
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

        private List<KMPartition> Rerank(string question, List<KMPartition> partitions)
        {
            using var serviceScope = _serviceProvider.CreateScope();
            var rerankService = serviceScope.ServiceProvider.GetRequiredService<IRerankService>();
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
    }
}
