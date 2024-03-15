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
using System;
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
        private readonly MemoryServerless _memoryServerless;
        private readonly string _promptTemplate;

        public RAGConversationService(Kernel kernel, LlmApp app, IServiceProvider serviceProvider, MemoryServerless memoryServerless)
        {
            _kernel = kernel;
            _app = app;
            _serviceProvider = serviceProvider;
            _configuration = serviceProvider.GetRequiredService<IConfiguration>();
            _llmAppKnowledgeRepository = _serviceProvider.GetService<IRepository<LlmAppKnowledge>>();
            _memoryServerless = memoryServerless;
            _promptTemplate = LoadPromptTemplate("RAGPrompt.txt");
        }

        public async Task InvokeAsync(OpenAIModel model, HttpContext HttpContext, string input)
        {
            if (model.stream)
            {
                var stramingResult = new OpenAIStreamResult();
                stramingResult.created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                stramingResult.choices = new List<StreamChoicesModel>() { new StreamChoicesModel() { delta = new OpenAIMessage() { role = "assistant" } } };
                await InvokeWithKnowledgeStreaming(HttpContext, stramingResult, input);
                HttpContext.Response.ContentType = "application/json";
                await HttpContext.Response.WriteAsync(JsonConvert.SerializeObject(stramingResult));
                await HttpContext.Response.CompleteAsync();
            }
            else
            {
                var result = new OpenAIResult();
                result.created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                result.choices = new List<ChoicesModel>() { new ChoicesModel() { message = new OpenAIMessage() { role = "assistant" } } };
                result.choices[0].message.content = await InvokeWithKnowledge(input);
                HttpContext.Response.ContentType = "application/json";
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
            var context = BuildKnowledgeContext(input);

            var temperature = _app.Temperature / 100;
            var settings = new OpenAIPromptExecutionSettings() { Temperature = (double)temperature };

            var func = _kernel.CreateFunctionFromPrompt(_promptTemplate, settings);
            var chatResult = await _kernel.InvokeAsync(
                function: func,
                arguments: new KernelArguments() { ["context"] = context, ["name"] = "ChatGPT", ["question"] = input }
            );

            var answers = chatResult.GetValue<string>();
            if (!string.IsNullOrEmpty(answers)) return answers;

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
            HttpContext.Response.Headers.ContentType = new Microsoft.Extensions.Primitives.StringValues("text/event-stream");

            var context = BuildKnowledgeContext(input);

            var temperature = _app.Temperature / 100;
            OpenAIPromptExecutionSettings settings = new() { Temperature = (double)temperature };
            var func = _kernel.CreateFunctionFromPrompt(_app.Prompt, settings);
            var chatResult = _kernel.InvokeStreamingAsync<StreamingChatMessageContent>(
                function: func,
                arguments: new KernelArguments() { ["context"] = context, ["name"] = "ChatGPT", ["question"] = input }
            );

            await foreach (var content in chatResult)
            {
                result.choices[0].delta.content = content.Content ?? string.Empty;
                string message = $"data: {JsonConvert.SerializeObject(result)}\n\n";
                await HttpContext.Response.WriteAsync(message, Encoding.UTF8);
                await HttpContext.Response.Body.FlushAsync();
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }

            await HttpContext.Response.WriteAsync("data: [DONE]");
            await HttpContext.Response.Body.FlushAsync();
            await HttpContext.Response.CompleteAsync();
        }

        /// <summary>
        /// 加载提示词模板
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private string LoadPromptTemplate(string fileName)
        {
            var promptDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Common/Prompts");
            var promptTemplate = Path.Combine(promptDirectory, fileName);
            if (!File.Exists(promptTemplate))
                throw new ArgumentException($"The prompt template file '{promptTemplate}' can not be found.");

            return File.ReadAllText(promptTemplate);
        }

        /// <summary>
        /// 构建知识库上下文
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private async Task<string> BuildKnowledgeContext(string input)
        {
            var filters = new List<MemoryFilter>();
            var llmKappKnowledges = await _llmAppKnowledgeRepository.FindAsync(x => x.AppId == _app.Id);
            if (llmKappKnowledges.Any())
            {
                foreach (var knowledgeBase in llmKappKnowledges)
                {
                    filters.Add(new MemoryFilter().ByTag(KernelMemoryTags.KnowledgeBaseId, knowledgeBase.KnowledgeBaseId.ToString()));
                }
            }

            // 构建上下文
            var contextBuilder = new StringBuilder();
            var searchResult = await _memoryServerless.SearchAsync(input, filters: filters, minRelevance: 0, limit: 5);

            if (searchResult.Results.Any())
            {
                foreach (var citation in searchResult.Results)
                {
                    foreach (var part in citation.Partitions)
                    {
                        contextBuilder.AppendLine($"fileName:{citation.SourceName}; Relevance:{(part.Relevance * 100).ToString("F2")}%; Content: {part.Text}");
                    }
                }

                return contextBuilder.ToString();
            }

            return string.Empty;
        }
    }
}
