using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Text;
using PostgreSQL.Embedding.LLmServices.Extensions;
using Masuit.Tools;
using Microsoft.AspNetCore.Http;

namespace PostgreSQL.Embedding.LlmServices
{
    public class GenericConversationService
    {
        private readonly Kernel _kernel;
        private readonly LlmApp _app;
        private readonly string _promptTemplate;
        private readonly string _defaultPrompt = "You are a helpful AI bot. You must answer the question in Chinese.";
        private readonly IChatHistoryService _chatHistoryService;
        private readonly IServiceProvider _serviceProvider;
        private readonly PromptTemplateService _promptTemplateService;
        private string _conversationId;
        private readonly Random _random = new Random();
        public GenericConversationService(Kernel kernel, LlmApp app, IServiceProvider serviceProvider, IChatHistoryService chatHistoryService)
        {
            _kernel = kernel;
            _app = app;
            _serviceProvider = serviceProvider;
            _promptTemplateService = _serviceProvider.GetService<PromptTemplateService>();
            _promptTemplate = _promptTemplateService.LoadPromptTemplate("Default.txt");
            _chatHistoryService = chatHistoryService;
        }

        public async Task InvokeAsync(OpenAIModel model, HttpContext httpContext, string input)
        {
            _conversationId = httpContext.GetOrCreateConversationId();
            var conversationName = httpContext.GetConversationName();
            await _chatHistoryService.AddUserMessage(_app.Id, _conversationId, input);
            await _chatHistoryService.AddConversation(_app.Id, _conversationId, conversationName);

            if (model.stream)
            {
                var stramingResult = new OpenAIStreamResult();
                stramingResult.created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                stramingResult.choices = new List<StreamChoicesModel>() { new StreamChoicesModel() { delta = new OpenAIMessage() { role = "assistant" } } };
                await InvokeStramingChat(httpContext, stramingResult, input);
                if (!httpContext.Response.HasStarted)
                {
                    httpContext.Response.ContentType = "application/json";
                    await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(stramingResult));
                    await httpContext.Response.CompleteAsync();
                }
            }
            else
            {
                var result = new OpenAICompatibleResult();
                result.Choices = new List<OpenAICompatibleChoicesModel>() { new OpenAICompatibleChoicesModel() { message = new OpenAIMessage() { role = "assistant" } } };
                result.Choices[0].message.content = await InvokeChat(input);
                if (!httpContext.Response.HasStarted)
                {
                    httpContext.Response.ContentType = "application/json";
                    await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(result));
                    await httpContext.Response.CompleteAsync();
                }
            }
        }

        /// <summary>
        /// 流式聊天
        /// </summary>
        /// <param name="HttpContext"></param>
        /// <param name="result"></param>
        /// <param name="input"></param>
        /// <returns></returns>
        private async Task InvokeStramingChat(HttpContext HttpContext, OpenAIStreamResult result, string input)
        {
            if (!HttpContext.Response.HasStarted)
            {
                HttpContext.Response.Headers.ContentType = new Microsoft.Extensions.Primitives.StringValues("text/event-stream");
            }


            if (string.IsNullOrEmpty(_app.Prompt))
                _app.Prompt = _defaultPrompt;

            var temperature = _app.Temperature / 100;
            var settings = new OpenAIPromptExecutionSettings() { Temperature = (double)temperature };
            var func = _kernel.CreateFunctionFromPrompt(_promptTemplate, settings);
            var chatResult = _kernel.InvokeStreamingAsync<StreamingChatMessageContent>(
                function: func,
                arguments: new KernelArguments() { ["input"] = input, ["system"] = _app.Prompt }
            );

            var answerBuilder = new StringBuilder();
            var random = new Random();
            await foreach (var content in chatResult)
            {
                if (!string.IsNullOrEmpty(content.Content)) answerBuilder.Append(content.Content);
                result.choices[0].delta.content = content.Content ?? string.Empty;
                string message = $"data: {JsonConvert.SerializeObject(result)}\n";
                await HttpContext.Response.WriteAsync(message, Encoding.UTF8);
                await HttpContext.Response.Body.FlushAsync();
                await Task.Delay(TimeSpan.FromMilliseconds(random.Next(10, 200)));
            }

            await _chatHistoryService.AddSystemMessage(_app.Id, _conversationId, answerBuilder.ToString());
            await HttpContext.Response.WriteAsync("data: [DONE]");
            await HttpContext.Response.Body.FlushAsync();
            await HttpContext.Response.CompleteAsync();
        }

        /// <summary>
        /// 普通聊天
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private async Task<string> InvokeChat(string input)
        {
            if (string.IsNullOrEmpty(_app.Prompt))
                _app.Prompt = _defaultPrompt;

            var temperature = _app.Temperature / 100;
            var settings = new OpenAIPromptExecutionSettings() { Temperature = (double)temperature };
            var func = _kernel.CreateFunctionFromPrompt(_promptTemplate, settings);
            var chatResult = await _kernel.InvokeAsync(
                function: func,
                arguments: new KernelArguments() { ["input"] = input, ["system"] = _app.Prompt }
            );
            var answer = chatResult.GetValue<string>();
            if (!string.IsNullOrEmpty(answer))
            {
                await _chatHistoryService.AddSystemMessage(_app.Id, _conversationId, answer);
                return answer;
            }
            return string.Empty;
        }
    }
}
