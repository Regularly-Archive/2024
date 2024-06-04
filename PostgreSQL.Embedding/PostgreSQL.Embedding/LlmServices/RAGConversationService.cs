using Azure.Search.Documents.Models;
using DocumentFormat.OpenXml.InkML;
using Microsoft.AspNetCore.Http;
using Microsoft.KernelMemory;
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
using System.Net.Http;
using System.Text;

namespace PostgreSQL.Embedding.LlmServices
{
    public class RAGConversationService
    {
        private readonly Kernel _kernel;
        private readonly LlmApp _app;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly IRepository<LlmAppKnowledge> _llmAppKnowledgeRepository;
        private readonly IRepository<KnowledgeBase> _knowledgeBaseRepository;
        private readonly IMemoryService _memoryService;
        private readonly IChatHistoryService _chatHistoryService;
        private readonly PromptTemplateService _promptTemplateService;
        private readonly ILogger<RAGConversationService> _logger;
        private readonly CallablePromptTemplate _promptTemplate;
        private readonly CallablePromptTemplate _rewritePromptTemplate;
        private string _conversationId;
        private readonly IRerankService _rerankService;

        public RAGConversationService(
            Kernel kernel,
            LlmApp app,
            IServiceProvider serviceProvider,
            IMemoryService memoryService,
            IChatHistoryService chatHistoryService
        )
        {
            _kernel = kernel;
            _app = app;
            _serviceProvider = serviceProvider;
            _configuration = serviceProvider.GetRequiredService<IConfiguration>();
            _llmAppKnowledgeRepository = _serviceProvider.GetService<IRepository<LlmAppKnowledge>>();
            _knowledgeBaseRepository = _serviceProvider.GetService<IRepository<KnowledgeBase>>();
            _memoryService = memoryService;
            _promptTemplateService = _serviceProvider.GetService<PromptTemplateService>();
            _promptTemplate = _promptTemplateService.LoadPromptTemplate("RAGPrompt.txt");
            _rewritePromptTemplate = _promptTemplateService.LoadPromptTemplate("RewritePrompt.txt");
            _chatHistoryService = chatHistoryService;
            _logger = _serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<RAGConversationService>();
            _rerankService = _serviceProvider.GetRequiredService<IRerankService>();
        }

        public async Task InvokeAsync(OpenAIModel model, HttpContext HttpContext, string input)
        {
            _conversationId = HttpContext.GetOrCreateConversationId();
            var conversationName = HttpContext.GetConversationName();
            await _chatHistoryService.AddUserMessage(_app.Id, _conversationId, input);
            await _chatHistoryService.AddConversation(_app.Id, _conversationId, conversationName);

            if (model.stream)
            {
                await InvokeWithKnowledgeStreaming(HttpContext, input);
            }
            else
            {
                await InvokeWithKnowledge(HttpContext, input);
            }
        }

        /// <summary>
        /// 知识库普通聊天
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private async Task InvokeWithKnowledge(HttpContext HttpContext, string input)
        {
            var context = await BuildKnowledgeContext(input);

            var temperature = _app.Temperature / 100;
            var executionSettings = new OpenAIPromptExecutionSettings() { Temperature = (double)temperature };

            _promptTemplate.AddVariable("name", "ChatGPT");
            _promptTemplate.AddVariable("context", context);
            _promptTemplate.AddVariable("question", input);
            _promptTemplate.AddVariable("empty_answer", Common.Constants.DefaultEmptyAnswer);
            var chatResult = await _promptTemplate.InvokeAsync(_kernel, executionSettings);

            var answer = chatResult.GetValue<string>();
            if (!string.IsNullOrEmpty(answer))
            {
                await _chatHistoryService.AddSystemMessage(_app.Id, _conversationId, answer);
                await HttpContext.WriteChatCompletion(input);
            }
        }

        /// <summary>
        /// 知识库流式聊天
        /// </summary>
        /// <param name="HttpContext"></param>
        /// <param name="result"></param>
        /// <param name="input"></param>
        /// <returns></returns>
        private async Task InvokeWithKnowledgeStreaming(HttpContext HttpContext, string input)
        {
            if (!HttpContext.Response.HasStarted)
            {
                HttpContext.Response.Headers.ContentType = new Microsoft.Extensions.Primitives.StringValues("text/event-stream");
            }

            var context = await BuildKnowledgeContext(input);

            var temperature = _app.Temperature / 100;
            var executionSettings = new OpenAIPromptExecutionSettings() { Temperature = (double)temperature };

            _promptTemplate.AddVariable("name", "ChatGPT");
            _promptTemplate.AddVariable("context", context);
            _promptTemplate.AddVariable("question", input);
            _promptTemplate.AddVariable("empty_answer", Common.Constants.DefaultEmptyAnswer);
            var chatResult = _promptTemplate.InvokeStreamingAsync(_kernel, executionSettings);

            var answerBuilder = new StringBuilder();
            await foreach (var content in chatResult)
            {
                if (!string.IsNullOrEmpty(content.Content)) answerBuilder.Append(content.Content);
            }

            await _chatHistoryService.AddSystemMessage(_app.Id, _conversationId, answerBuilder.ToString());

            await HttpContext.WriteStreamingChatCompletion(chatResult);
        }


        /// <summary>
        /// 构建知识库上下文
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private async Task<string> BuildKnowledgeContext(string question)
        {
            var searchResults = new List<KMCitation>();
            var llmKappKnowledges = await _llmAppKnowledgeRepository.FindAsync(x => x.AppId == _app.Id);
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
            var chunks = partitions.Select(x => new
            {
                FileName = x.FileName,
                Relevance = x.Relevance,
                Text = x.Text
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

            var jsonFormatContext = JsonConvert.SerializeObject(chunks);
            return jsonFormatContext;
        }

        private async Task<List<KMCitation>> RetrieveAsync(KnowledgeBase knowledgeBase, string question)
        {
            var limit = knowledgeBase.RetrievalLimit.HasValue ? 
                knowledgeBase.RetrievalLimit.Value : PostgreSQL.Embedding.Common.Constants.DefaultRetrievalLimit;

            var minRelevance = knowledgeBase.RetrievalRelevance.HasValue 
                ? knowledgeBase.RetrievalRelevance.Value / 100 : PostgreSQL.Embedding.Common.Constants.DefaultRetrievalRelevance;

            using var serviceScope = _serviceProvider.CreateScope();
            if (knowledgeBase.RetrievalType == (int)RetrievalType.Vectors)
            {
                // 向量检索
                var knowledgeBaseService = _memoryService.AsKnowledgeBaseService(serviceScope.ServiceProvider);
                var searchResult = await knowledgeBaseService.SearchAsync(knowledgeBase.Id, question, (double)minRelevance, limit);
                return searchResult.RelevantSources;

            }
            else if (knowledgeBase.RetrievalType == (int)RetrievalType.FullText)
            {
                // 全文检索
                var fullTextService = _memoryService.AsFullTextSearchService(serviceScope.ServiceProvider);
                var searchResult = await fullTextService.SearchAsync(knowledgeBase.Id, question, (double)minRelevance, limit);
                return searchResult.RelevantSources;
            }
            else
            {
                // 混合检索
                var searchResults = new List<KMCitation>();
                var knowledgeBaseService = _memoryService.AsKnowledgeBaseService(serviceScope.ServiceProvider);
                var vectorSearchResult = await knowledgeBaseService.SearchAsync(knowledgeBase.Id, question, (double)minRelevance, limit);
                if (vectorSearchResult.RelevantSources.Any()) searchResults.AddRange(vectorSearchResult.RelevantSources);

                var fullTextService = _memoryService.AsFullTextSearchService(serviceScope.ServiceProvider);
                var fullTextSearchResult = await fullTextService.SearchAsync(knowledgeBase.Id, question, (double)minRelevance, limit);
                if (fullTextSearchResult.RelevantSources.Any()) searchResults = fullTextSearchResult.RelevantSources;

                return searchResults;
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
    }
}
