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
        private readonly string _promptTemplate;
        private readonly string _rewritePromptTemplate;
        private readonly float _minRelevance = 0;
        private readonly int _limit = 5;
        private string _conversationId;

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
        }

        public async Task InvokeAsync(OpenAIModel model, HttpContext HttpContext, string input)
        {
            _conversationId = HttpContext.GetOrCreateConversationId();
            var conversationName = HttpContext.GetConversationName();
            await _chatHistoryService.AddUserMessage(_app.Id, _conversationId, input);
            await _chatHistoryService.AddConversation(_app.Id, _conversationId, conversationName);

            if (model.stream)
            {
                var stramingResult = new OpenAIStreamResult();
                stramingResult.created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                stramingResult.choices = new List<StreamChoicesModel>() { new StreamChoicesModel() { delta = new OpenAIMessage() { role = "assistant" } } };
                await InvokeWithKnowledgeStreaming(HttpContext, stramingResult, input);
                if (!HttpContext.Response.HasStarted)
                {
                    HttpContext.Response.ContentType = "application/json";
                }
                await HttpContext.Response.WriteAsync(JsonConvert.SerializeObject(stramingResult));
                await HttpContext.Response.CompleteAsync();
            }
            else
            {
                var result = new OpenAICompatibleResult();
                result.Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                result.Choices = new List<OpenAICompatibleChoicesModel>() { new OpenAICompatibleChoicesModel() { message = new OpenAIMessage() { role = "assistant" } } };
                result.Choices[0].message.content = await InvokeWithKnowledge(input);
                if (!HttpContext.Response.HasStarted)
                {
                    HttpContext.Response.ContentType = "application/json";
                }
                await HttpContext.Response.WriteAsync(JsonConvert.SerializeObject(result));
                await HttpContext.Response.CompleteAsync();
            }

        }

        /// <summary>
        /// 知识库普通聊天
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private async Task<string> InvokeWithKnowledge(string input)
        {
            var context = await BuildKnowledgeContext(input);

            var temperature = _app.Temperature / 100;
            var settings = new OpenAIPromptExecutionSettings() { Temperature = (double)temperature };

            var func = _kernel.CreateFunctionFromPrompt(_promptTemplate, settings);
            var chatResult = await _kernel.InvokeAsync(
                function: func,
                arguments: new KernelArguments() { ["context"] = context, ["name"] = "ChatGPT", ["empty_answer"] = Common.Constants.DefaultEmptyAnswer, ["question"] = input }
            );

            var answer = chatResult.GetValue<string>();
            if (!string.IsNullOrEmpty(answer))
            {
                await _chatHistoryService.AddSystemMessage(_app.Id, _conversationId, answer);
                return answer;
            }

            return string.Empty;
        }

        /// <summary>
        /// 知识库流式聊天
        /// </summary>
        /// <param name="HttpContext"></param>
        /// <param name="result"></param>
        /// <param name="input"></param>
        /// <returns></returns>
        private async Task InvokeWithKnowledgeStreaming(HttpContext HttpContext, OpenAIStreamResult result, string input)
        {
            if (!HttpContext.Response.HasStarted)
            {
                HttpContext.Response.Headers.ContentType = new Microsoft.Extensions.Primitives.StringValues("text/event-stream");
            }

            var context = await BuildKnowledgeContext(input);

            var temperature = _app.Temperature / 100;
            OpenAIPromptExecutionSettings settings = new() { Temperature = (double)temperature };
            var func = _kernel.CreateFunctionFromPrompt(_promptTemplate, settings);
            var chatResult = _kernel.InvokeStreamingAsync<StreamingChatMessageContent>(
                function: func,
                arguments: new KernelArguments() { ["context"] = context, ["name"] = "ChatGPT", ["question"] = input }
            );

            var answerBuilder = new StringBuilder();
            await foreach (var content in chatResult)
            {
                if (!string.IsNullOrEmpty(content.Content)) answerBuilder.Append(content.Content);
                result.choices[0].delta.content = content.Content ?? string.Empty;
                string message = $"data: {JsonConvert.SerializeObject(result)}\n";
                await HttpContext.Response.WriteAsync(message, Encoding.UTF8);
                await HttpContext.Response.Body.FlushAsync();
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }

            await _chatHistoryService.AddSystemMessage(_app.Id, _conversationId, answerBuilder.ToString());
            await HttpContext.Response.WriteAsync("data: [DONE]");
            await HttpContext.Response.Body.FlushAsync();
            await HttpContext.Response.CompleteAsync();
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
                var similarQuestions = await RewriteAsync(question);
                _logger.LogInformation($"完成用户输入重写，共生成{similarQuestions.Count}个相似问题.");
                if (similarQuestions.Any()) { inputs.AddRange(similarQuestions); }
                foreach (var appKnowledge in llmKappKnowledges)
                {
                    var knowledgeBase = await _knowledgeBaseRepository.GetAsync(appKnowledge.KnowledgeBaseId);
                    if (knowledgeBase == null) continue;

                    foreach (var input in inputs)
                    {
                        var retrieveResult = await RetrieveAsync(knowledgeBase, _memoryService, input);
                        if (retrieveResult != null && retrieveResult.Any()) searchResults.AddRange(retrieveResult);
                    }
                }
            }

            // 构建上下文
            var partitions = searchResults.SelectMany(x => x.Partitions).ToList();
            var chunks = partitions.Select(x => new
            {
                FileName = x.FileName,
                Relevance = x.Relevance,
                Text = x.Text
            })
            .OrderByDescending(x => x.Relevance)
            .Take(10)
            .ToList();

            var maxRelevance = chunks.Max(x => x.Relevance);
            var minRelevance = chunks.Min(x => x.Relevance);
            _logger.LogInformation($"共检索到 {chunks.Count} 个文档块，相似度区间[{minRelevance},{maxRelevance}]");

            var jsonFormatContext = JsonConvert.SerializeObject(chunks);
            return jsonFormatContext;
        }

        private async Task<List<KMCitation>> RetrieveAsync(KnowledgeBase knowledgeBase, IMemoryService memoryService, string question)
        {
            using var serviceScope = _serviceProvider.CreateScope();
            if (knowledgeBase.RetrievalType == (int)RetrievalType.Vectors)
            {
                // 向量检索
                var knowledgeBaseService = _memoryService.AsKnowledgeBaseService(serviceScope.ServiceProvider);
                var searchResult = await knowledgeBaseService.SearchAsync(knowledgeBase.Id, question, _minRelevance, _limit);
                return searchResult.RelevantSources;

            }
            else if (knowledgeBase.RetrievalType == (int)RetrievalType.FullText)
            {
                // 全文检索
                var fullTextService = _memoryService.AsFullTextSearchService(serviceScope.ServiceProvider);
                var searchResult = await fullTextService.SearchAsync(knowledgeBase.Id, question, _minRelevance, _limit);
                return searchResult.RelevantSources;
            }
            else
            {
                // 混合检索
                var searchResults = new List<KMCitation>();
                var knowledgeBaseService = _memoryService.AsKnowledgeBaseService(serviceScope.ServiceProvider);
                var vectorSearchResult = await knowledgeBaseService.SearchAsync(knowledgeBase.Id, question, _minRelevance, _limit);
                if (vectorSearchResult.RelevantSources.Any()) searchResults.AddRange(vectorSearchResult.RelevantSources);

                var fullTextService = _memoryService.AsFullTextSearchService(serviceScope.ServiceProvider);
                var fullTextSearchResult = await fullTextService.SearchAsync(knowledgeBase.Id, question, _minRelevance, _limit);
                if (fullTextSearchResult.RelevantSources.Any()) searchResults = fullTextSearchResult.RelevantSources;

                return searchResults;
            }
        }

        private async Task<List<string>> RewriteAsync(string question)
        {
            var similarQuestions = new List<string>();
            try
            {
                var settings = new OpenAIPromptExecutionSettings() { Temperature = 0f };

                var func = _kernel.CreateFunctionFromPrompt(_rewritePromptTemplate, settings);
                var invokeResult = await _kernel.InvokeAsync(
                function: func,
                    arguments: new KernelArguments() { ["question"] = question }
                );

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
    }
}
