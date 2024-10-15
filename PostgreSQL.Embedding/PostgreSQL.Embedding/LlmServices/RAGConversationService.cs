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

namespace PostgreSQL.Embedding.LlmServices
{
    public class RAGConversationService : BaseConversationService
    {
        private readonly Kernel _kernel;
        private readonly LlmApp _app;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly IRepository<LlmAppKnowledge> _llmAppKnowledgeRepository;
        private readonly IRepository<KnowledgeBase> _knowledgeBaseRepository;
        private readonly IMemoryService _memoryService;
        private readonly IChatHistoriesService _chatHistoriesService;
        private readonly PromptTemplateService _promptTemplateService;
        private readonly ILogger<RAGConversationService> _logger;
        private readonly CallablePromptTemplate _promptTemplate;
        private readonly CallablePromptTemplate _rewritePromptTemplate;
        private string _conversationId;
        private long _messageReferenceId;
        private readonly IRerankService _rerankService;
        private Regex _regexCitations = new Regex(@"\[(\d+)\]");

        public RAGConversationService(
            Kernel kernel,
            LlmApp app,
            IServiceProvider serviceProvider,
            IMemoryService memoryService,
            IChatHistoriesService chatHistoriesService
        )
            : base(kernel, chatHistoriesService)
        {
            _kernel = kernel;
            _app = app;
            _serviceProvider = serviceProvider;
            _configuration = serviceProvider.GetRequiredService<IConfiguration>();
            _llmAppKnowledgeRepository = _serviceProvider.GetService<IRepository<LlmAppKnowledge>>();
            _knowledgeBaseRepository = _serviceProvider.GetService<IRepository<KnowledgeBase>>();
            _memoryService = memoryService;
            _promptTemplateService = _serviceProvider.GetService<PromptTemplateService>();
            _promptTemplate = _promptTemplateService.LoadTemplate("RAGPrompt.txt");
            _rewritePromptTemplate = _promptTemplateService.LoadTemplate("RewritePrompt.txt");
            _chatHistoriesService = chatHistoriesService;
            _logger = _serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<RAGConversationService>();
            _rerankService = _serviceProvider.GetRequiredService<IRerankService>();
        }

        public async Task InvokeAsync(OpenAIModel model, HttpContext HttpContext, string input, CancellationToken cancellationToken = default)
        {
            _conversationId = HttpContext.GetOrCreateConversationId();
            var conversationName = HttpContext.GetConversationName();

            // 如果是重新生成，则删除最后一条 AI 消息
            var conversationFlag = HttpContext.GetConversationFlag();
            if (!conversationFlag)
            {
                _messageReferenceId = await _chatHistoriesService.AddUserMessageAsync(_app.Id, _conversationId, input);
                await _chatHistoriesService.AddConversationAsync(_app.Id, _conversationId, conversationName);
            }
            else
            {
                await RemoveLastChatMessage(_app.Id, _conversationId);
            }

            var conversationTask = model.stream
                ? InvokeWithKnowledgeStreaming(HttpContext, input, cancellationToken)
                : InvokeWithKnowledge(HttpContext, input);

            await conversationTask;
        }

        /// <summary>
        /// 知识库普通聊天
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private async Task InvokeWithKnowledge(HttpContext HttpContext, string input)
        {
            var citations = await BuildKnowledgeCitations(input);
            var jsonFormatContext = JsonConvert.SerializeObject(citations);

            var temperature = _app.Temperature / 100;
            var executionSettings = new OpenAIPromptExecutionSettings() { Temperature = (double)temperature };

            var histories = await GetHistoricalMessagesAsync(_app.Id, _conversationId, _app.MaxMessageRounds);

            _promptTemplate.AddVariable("name", "ChatGPT");
            _promptTemplate.AddVariable("context", jsonFormatContext);
            _promptTemplate.AddVariable("question", input);
            _promptTemplate.AddVariable("empty_answer", Common.Constants.DefaultEmptyAnswer);
            _promptTemplate.AddVariable("histories", histories);
            var chatResult = await _promptTemplate.InvokeAsync(_kernel, executionSettings);

            var answer = chatResult.GetValue<string>();
            if (!string.IsNullOrEmpty(answer))
            {
                if (answer.IndexOf(Common.Constants.DefaultEmptyAnswer) != -1)
                {
                    answer = Common.Constants.DefaultEmptyAnswer;
                    var messageId = await _chatHistoriesService.AddSystemMessageAsync(_app.Id, _conversationId, answer);
                    HttpContext.Response.Headers[Common.Constants.HttpResponseHeader_ReferenceMessageId] = _messageReferenceId.ToString();
                    await HttpContext.WriteChatCompletion(input, messageId);
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

                    var answerBuilder = new StringBuilder();
                    answerBuilder.AppendLine(answer);
                    answerBuilder.AppendLine();
                    answerBuilder.AppendLine(markdownFormatContext);

                    var messageId = await _chatHistoriesService.AddSystemMessageAsync(_app.Id, _conversationId, answerBuilder.ToString());
                    HttpContext.Response.Headers[Common.Constants.HttpResponseHeader_ReferenceMessageId] = _messageReferenceId.ToString();
                    await HttpContext.WriteChatCompletion(answerBuilder.ToString(), messageId);
                }
            }
        }

        /// <summary>
        /// 知识库流式聊天
        /// </summary>
        /// <param name="HttpContext"></param>
        /// <param name="result"></param>
        /// <param name="input"></param>
        /// <returns></returns>
        private async Task InvokeWithKnowledgeStreaming(HttpContext HttpContext, string input, CancellationToken cancellationToken = default)
        {
            if (!HttpContext.Response.HasStarted)
            {
                HttpContext.Response.Headers.ContentType = new Microsoft.Extensions.Primitives.StringValues("text/event-stream");
            }

            var citations = await BuildKnowledgeCitations(input);
            var jsonFormatContext = JsonConvert.SerializeObject(citations);

            var temperature = _app.Temperature / 100;
            var executionSettings = new OpenAIPromptExecutionSettings() { Temperature = (double)temperature };

            var histories = await GetHistoricalMessagesAsync(_app.Id, _conversationId, _app.MaxMessageRounds);

            _promptTemplate.AddVariable("name", "ChatGPT");
            _promptTemplate.AddVariable("context", jsonFormatContext);
            _promptTemplate.AddVariable("question", input);
            _promptTemplate.AddVariable("empty_answer", Common.Constants.DefaultEmptyAnswer);
            _promptTemplate.AddVariable("histories", histories);
            var chatResult = await _promptTemplate.InvokeAsync(_kernel, executionSettings);

            var llmResponse = chatResult.GetValue<string>();
            if (llmResponse != null && llmResponse.IndexOf(Common.Constants.DefaultEmptyAnswer) != -1)
            {
                llmResponse = Common.Constants.DefaultEmptyAnswer;
                var messageId = await _chatHistoriesService.AddSystemMessageAsync(_app.Id, _conversationId, llmResponse);
                HttpContext.Response.Headers[Common.Constants.HttpResponseHeader_ReferenceMessageId] = _messageReferenceId.ToString();
                await HttpContext.WriteStreamingChatCompletion(llmResponse, messageId, cancellationToken);
            } 
            else
            {
                var index = 0;
                var matchedCitationNumbers = _regexCitations.Matches(llmResponse).Select(x => int.Parse(x.Groups[1].Value)).ToList();
                var newCitationNumbers = new List<LlmCitationMappingModel>();
                foreach (var citationNumber in matchedCitationNumbers)
                {
                    if (!newCitationNumbers.Any(x => x.OriginIndex == citationNumber))
                    {
                        index += 1;
                        newCitationNumbers.Add(new LlmCitationMappingModel() { NewIndex = index, OriginIndex = citationNumber });
                    }
                }

                var generatedCitations = citations.Where(x => matchedCitationNumbers.Contains(x.Index)).Select(x =>
                {
                    var newIndex = newCitationNumbers.FirstOrDefault(k => k.OriginIndex == x.Index).NewIndex;
                    return new LlmCitationModel() { Index = newIndex, Url = x.Url };
                })
                .OrderBy(x => x.Index)
                .Select(x => $"[{x.Index}]: {x.Url}")
                .ToList();

                var markdownFormatContext = string.Join("\r\n", generatedCitations);

                // 对答案中的引用信息重新排序
                foreach (var ciation in newCitationNumbers)
                {
                    llmResponse = llmResponse.Replace($"[{ciation.OriginIndex}]", $"[{ciation.NewIndex}]");
                }

                var answerBuilder = new StringBuilder();
                answerBuilder.AppendLine(llmResponse);
                answerBuilder.AppendLine();
                answerBuilder.AppendLine(markdownFormatContext);

                var messageId = await _chatHistoriesService.AddSystemMessageAsync(_app.Id, _conversationId, answerBuilder.ToString());
                HttpContext.Response.Headers[Common.Constants.HttpResponseHeader_ReferenceMessageId] = _messageReferenceId.ToString();
                await HttpContext.WriteStreamingChatCompletion(answerBuilder.ToString(), messageId, cancellationToken);
            }
        }


        /// <summary>
        /// 构建知识库上下文
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private async Task<List<LlmCitationModel>> BuildKnowledgeCitations(string question)
        {
            var searchResults = new List<KMCitation>();
            var llmKappKnowledges = await _llmAppKnowledgeRepository.FindListAsync(x => x.AppId == _app.Id);
            if (llmKappKnowledges.Any())
            {
                var inputs = new List<string> { question };

                // 查询重写
                if (_app.EnableRewrite)
                {
                    var similarQuestions = await RewriteAsync(question);
                    _logger.LogInformation($"查询重写，共生成{similarQuestions.Count}个相似问题：{JsonConvert.SerializeObject(similarQuestions)}.");
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
            }


            var partitions = searchResults.SelectMany(x => x.Partitions).ToList();

            // 结果重排
            if (_app.EnableRerank)
            {
                partitions = Rerank(question, partitions);
            }

            // 构建上下文
            var chunks = partitions.Select((x,i) => new LlmCitationModel
            {
                Index =  i + 1,
                FileName = x.FileName,
                Relevance = x.Relevance,
                Text = $"[^{i+1}]: {x.Text}",
                Url = $"/api/KnowledgeBase/{x.KnowledgeBaseId}/chunks/{x.FileId}/{x.PartId}"
            })
            .OrderByDescending(x => x.Relevance)
            .Take(10)
            .ToList();

            if (chunks.Any())
            {
                var maxRelevance = chunks.Max(x => x.Relevance);
                var minRelevance = chunks.Min(x => x.Relevance);
                _logger.LogInformation($"共检索到 {chunks.Count} 个文档块，相似度区间[{minRelevance},{maxRelevance}]");
            }
            else
            {
                _logger.LogInformation($"未检索到符合条件的文档块");
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

        private async Task<List<string>> RewriteAsync(string question)
        {
            var similarQuestions = new List<string>();
            try
            {
                var executionSettings = new OpenAIPromptExecutionSettings() { Temperature = 0f };

                _rewritePromptTemplate.AddVariable("question", question);
                var invokeResult = await _rewritePromptTemplate.InvokeAsync(_kernel, executionSettings);

                var payload = invokeResult.GetValue<string>();
                if (string.IsNullOrEmpty(payload)) return similarQuestions;

                payload = payload.Replace("```json", "").Replace("```", "");
                var llmRewriteResult = JsonConvert.DeserializeObject<LlmRewriteResult>(payload);
                return llmRewriteResult.Output;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The rewrite flow has been stoped due to unexpected reason: {0}", ex.Message);
                return similarQuestions;
            }
        }

        private List<KMPartition> Rerank(string question, List<KMPartition> partitions)
        {
            if (!partitions.Any()) return partitions;

            try
            {
                var rerankResult = _rerankService.Sort(question, partitions, x => x.Text).ToList();
                foreach (var item in rerankResult)
                {
                    var score = item.Score;
                    item.Document.SetRelevance((float)score);
                }

                return rerankResult.Select(x => x.Document).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The rerank flow has been stoped due to unexpected reason: {0}", ex.Message);
                return partitions;
            }
        }

        private async Task RemoveLastChatMessage(long appId, string conversationId)
        {
            var messageList = await _chatHistoriesService.GetConversationMessagesAsync(appId, conversationId);
            messageList = messageList.OrderBy(x => x.CreatedAt).ToList();

            _messageReferenceId = messageList.LastOrDefault(x => x.IsUserMessage).Id;

            var lastMessage = messageList.LastOrDefault();
            if (lastMessage != null && !lastMessage.IsUserMessage)
                await _chatHistoriesService.DeleteConversationMessageAsync(lastMessage.Id);

        }
    }
}
