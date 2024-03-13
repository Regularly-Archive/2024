using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Postgres;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Plugins.Memory;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using SqlSugar;
using System.Text;

namespace PostgreSQL.Embedding.LlmServices
{
    public class RAGConversationService
    {
        private readonly Kernel _kernel;
        private readonly LlmApp _app;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly IMemoryService _memoryService;
        private readonly SimpleClient<LlmAppKnowledge> _llmAppKnowledgeRepository;
        private readonly string _promptTemplate =
        @"
        You are a helpful AI bot. Your name is {{$name}}.
        Please answer the question only based on the following context:

        {{$context}}

        If the question is about your identity or role or name, answer '{{$name}}' directly, without the need to refer to the context
        If the context is not enough to support the generation of an answer, Please return ""I'm sorry, I can't anser your question."" immediately.
        You have an opportunity to refine the existing answer (only if needed) with current context.
        You must always answer the question in Chinese. 
        The Question is: {{$question}}.
        ";

        public RAGConversationService(Kernel kernel, LlmApp app, IServiceProvider serviceProvider, IMemoryService memoryService)
        {
            _kernel = kernel;
            _app = app;
            _serviceProvider = serviceProvider;
            _configuration = serviceProvider.GetRequiredService<IConfiguration>();
            _memoryService = memoryService;
            _llmAppKnowledgeRepository = _serviceProvider.GetService<SimpleClient<LlmAppKnowledge>>();
        }

        public async Task HandleKnowledge(HttpContext HttpContext, string input)
        {
            var result = new OpenAIResult();
            result.created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            result.choices = new List<ChoicesModel>() { new ChoicesModel() { message = new OpenAIMessage() { role = "assistant" } } };
            result.choices[0].message.content = await QueryWithMemories(input);
            HttpContext.Response.ContentType = "application/json";
            await HttpContext.Response.WriteAsync(JsonConvert.SerializeObject(result));
            await HttpContext.Response.CompleteAsync();
        }

        private async Task<string> QueryWithMemories(string input)
        {
            var _memory = await _memoryService.CreateByApp<MemoryServerless>(_app);
            string result = "";

            // Todo: 从 App 找出关联的知识库
            var filters = new List<MemoryFilter>();
            var llmKappKnowledges = await _llmAppKnowledgeRepository.GetListAsync(x => x.AppId == _app.Id);
            if (llmKappKnowledges.Any())
            {
                foreach (var knowledgeBase in llmKappKnowledges)
                {
                    filters.Add(new MemoryFilter().ByTag(KernelMemoryTags.KnowledgeBaseId, knowledgeBase.KnowledgeBaseId.ToString()));
                }
            }

            // 构建上下文
            var contextBuilder = new StringBuilder();
            var searchResult = await _memory.SearchAsync(input, filters: filters, minRelevance: 0, limit: 5);
            
            if (searchResult.Results.Any())
            {
                foreach (var citation in searchResult.Results)
                {
                    foreach (var part in citation.Partitions)
                    {
                         contextBuilder.AppendLine($"fileName:{citation.SourceName}; Relevance:{(part.Relevance * 100).ToString("F2")}%]; Content: {part.Text}");
                    }
                }

                var temperature = _app.Temperature / 100;
                var settings = new OpenAIPromptExecutionSettings() { Temperature = (double)temperature };
                var func = _kernel.CreateFunctionFromPrompt(_promptTemplate, settings);
                var chatResult = await _kernel.InvokeAsync(
                    function: func,
                    arguments: new KernelArguments() { ["context"] = contextBuilder.ToString(), ["name"] = "ChatGPT", ["question"] = input }
                );

                var answers = chatResult.GetValue<string>();
                if (!string.IsNullOrEmpty(answers)) return answers;
            }

            return result;
        }
    }
}
