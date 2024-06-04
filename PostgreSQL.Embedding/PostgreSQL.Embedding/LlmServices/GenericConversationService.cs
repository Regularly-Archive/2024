using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.LLmServices.Extensions;
using System.Text;
using Microsoft.SemanticKernel.Planning.Handlebars;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.SemanticKernel.ChatCompletion;
using PostgreSQL.Embedding.Utils;
using Microsoft.EntityFrameworkCore.Storage;
using Irony.Parsing;
using PostgreSQL.Embedding.Common;
using System.Reflection.Metadata.Ecma335;

namespace PostgreSQL.Embedding.LlmServices
{
    public class GenericConversationService
    {
        private readonly Kernel _kernel;
        private readonly LlmApp _app;
        private readonly CallablePromptTemplate _promptTemplate;
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
                await InvokeStreamingChat(httpContext, input);
            }
            else
            {
                await InvokeChat(httpContext, input);
            }
        }

        /// <summary>
        /// 流式聊天
        /// </summary>
        /// <param name="HttpContext"></param>
        /// <param name="result"></param>
        /// <param name="input"></param>
        /// <returns></returns>
        private async Task InvokeStreamingChat(HttpContext HttpContext, string input)
        {
            if (!HttpContext.Response.HasStarted)
            {
                HttpContext.Response.Headers.ContentType = new Microsoft.Extensions.Primitives.StringValues("text/event-stream");
            }

            var usePlugin = true;
            var chatResult = usePlugin
                ? await InvokeStreamingByPlannerAsync(_kernel, input)
                : await InvokeStreamingByKernelAsync(_kernel, input);

            var answerBuilder = new StringBuilder();
            await foreach (var content in chatResult)
            {
                if (!string.IsNullOrEmpty(content.Content)) answerBuilder.Append(content.Content);
            }

            await _chatHistoryService.AddSystemMessage(_app.Id, _conversationId, answerBuilder.ToString());

            await HttpContext.WriteStreamingChatCompletion(chatResult);
        }

        /// <summary>
        /// 普通聊天
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private async Task InvokeChat(HttpContext HttpContext, string input)
        {
            var usePlugin = true;

            var chatResult = usePlugin
                ? await InvokeByPlannerAsync(_kernel, input)
                : await InvokeByKernelAsync(_kernel, input);

            var answer = chatResult.GetValue<string>();
            if (!string.IsNullOrEmpty(answer))
            {
                await _chatHistoryService.AddSystemMessage(_app.Id, _conversationId, answer);
                await HttpContext.WriteChatCompletion(input);
            }
        }

        private async Task<FunctionResult> InvokeByPlannerAsync(Kernel kernel, string input)
        {
#pragma warning disable SKEXP0060
            var planner = new HandlebarsPlanner();
#pragma warning restore SKEXP0060
            try
            {
#pragma warning disable SKEXP0060
                var plan = await planner.CreatePlanAsync(kernel, input);
                var executionResult = await plan.InvokeAsync(kernel);
                var promptTemplate = _promptTemplateService.LoadPromptTemplate("AgentPrompt.txt");
                promptTemplate.AddVariable("input", input);
                promptTemplate.AddVariable("context", executionResult);
                return await promptTemplate.InvokeAsync(kernel);
#pragma warning restore SKEXP0060
            }
            catch (Exception ex)
            {
                return Constants.DefaultErrorAnswer.AsFunctionResult();
            }
        }

        private async Task<IAsyncEnumerable<StreamingChatMessageContent>> InvokeStreamingByPlannerAsync(Kernel kernel, string input)
        {
#pragma warning disable SKEXP0060
            var planner = new HandlebarsPlanner();
#pragma warning restore SKEXP0060
            try
            {
#pragma warning disable SKEXP0060
                var plan = await planner.CreatePlanAsync(kernel, input);
                var executionResult = await plan.InvokeAsync(kernel);
                var promptTemplate = _promptTemplateService.LoadPromptTemplate("AgentPrompt.txt");
                promptTemplate.AddVariable("input", input);
                promptTemplate.AddVariable("context", executionResult);
                return promptTemplate.InvokeStreamingAsync(kernel);
#pragma warning restore SKEXP0060
            }
            catch (Exception ex)
            {
                return Constants.DefaultErrorAnswer.AsStreamming();
            }
        }

        private Task<FunctionResult> InvokeByKernelAsync(Kernel kernel, string input)
        {
            if (string.IsNullOrEmpty(_app.Prompt))
                _app.Prompt = _defaultPrompt;

            var temperature = _app.Temperature / 100;
            var executionSettings = new OpenAIPromptExecutionSettings() { Temperature = (double)temperature };

            _promptTemplate.AddVariable("input", input);
            _promptTemplate.AddVariable("system", _app.Prompt);
            return _promptTemplate.InvokeAsync(kernel, executionSettings);
        }

        private Task<IAsyncEnumerable<StreamingChatMessageContent>> InvokeStreamingByKernelAsync(Kernel kernel, string input)
        {
            if (string.IsNullOrEmpty(_app.Prompt))
                _app.Prompt = _defaultPrompt;

            var temperature = _app.Temperature / 100;
            var executionSettings = new OpenAIPromptExecutionSettings() { Temperature = (double)temperature };

            _promptTemplate.AddVariable("input", input);
            _promptTemplate.AddVariable("system", _app.Prompt);
            return Task.FromResult(_promptTemplate.InvokeStreamingAsync(kernel, executionSettings));
        }
    }
}
