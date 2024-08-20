using AngleSharp.Css;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Planning.Handlebars;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.LLmServices.Extensions;
using PostgreSQL.Embedding.Planners;
using PostgreSQL.Embedding.Utils;
using System.Globalization;
using System.Text;


namespace PostgreSQL.Embedding.LlmServices
{
    public class GenericConversationService : BaseConversationService
    {
        private readonly Kernel _kernel;
        private readonly LlmApp _app;
        private readonly CallablePromptTemplate _promptTemplate;
        private readonly string _defaultPrompt = "You are a helpful AI bot. You must answer the question in Chinese.";
        private readonly IChatHistoriesService _chatHistoriesService;
        private readonly IServiceProvider _serviceProvider;
        private readonly PromptTemplateService _promptTemplateService;
        private string _conversationId;
        private readonly Random _random = new Random();
        public GenericConversationService(Kernel kernel, LlmApp app, IServiceProvider serviceProvider, IChatHistoriesService chatHistoriesService)
            : base(kernel, chatHistoriesService)
        {
            _kernel = kernel;
            _app = app;
            _serviceProvider = serviceProvider;
            _promptTemplateService = _serviceProvider.GetService<PromptTemplateService>();
            _promptTemplate = _promptTemplateService.LoadTemplate("Default.txt");
            _chatHistoriesService = chatHistoriesService;
        }

        public async Task InvokeAsync(OpenAIModel model, HttpContext httpContext, string input, CancellationToken cancellationToken = default)
        {
            _conversationId = httpContext.GetOrCreateConversationId();
            var conversationName = httpContext.GetConversationName();

            var conversationFlag = httpContext.GetConversationFlag();
            if (!conversationFlag)
            {
                await _chatHistoriesService.AddUserMessage(_app.Id, _conversationId, input);
                await _chatHistoriesService.AddConversation(_app.Id, _conversationId, conversationName);
            }
            else
            {
                await RemoveLastChatMessage(_app.Id, _conversationId);
            }


            var conversationTask = model.stream
                ? InvokeStreamingChat(httpContext, input, cancellationToken)
                : InvokeChat(httpContext, input, cancellationToken);

            await conversationTask;
        }

        /// <summary>
        /// 流式聊天
        /// </summary>
        /// <param name="HttpContext"></param>
        /// <param name="result"></param>
        /// <param name="input"></param>
        /// <returns></returns>
        private async Task InvokeStreamingChat(HttpContext HttpContext, string input, CancellationToken cancellationToken = default)
        {
            if (!HttpContext.Response.HasStarted)
                HttpContext.Response.Headers.ContentType = new Microsoft.Extensions.Primitives.StringValues("text/event-stream");

            var usePlugin = true;
            var chatResult = usePlugin
                ? await InvokeStreamingByStepwisePlannerAsync(_kernel, input, cancellationToken)
                : await InvokeStreamingByKernelAsync(_kernel, input, cancellationToken);

            var answerBuilder = new StringBuilder();
            await foreach (var content in chatResult)
            {
                if (!string.IsNullOrEmpty(content.Content)) answerBuilder.Append(content.Content);
            }

            await HttpContext.WriteStreamingChatCompletion(chatResult, cancellationToken);
            await _chatHistoriesService.AddSystemMessage(_app.Id, _conversationId, answerBuilder.ToString());
        }

        /// <summary>
        /// 普通聊天
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private async Task InvokeChat(HttpContext HttpContext, string input, CancellationToken cancellationToken = default)
        {
            var usePlugin = false;

            var chatResult = usePlugin
                ? await InvokeByPlannerAsync(_kernel, input)
                : await InvokeByKernelAsync(_kernel, input);

            var answer = chatResult.GetValue<string>();
            if (!string.IsNullOrEmpty(answer))
            {
                await _chatHistoriesService.AddSystemMessage(_app.Id, _conversationId, answer);
                await HttpContext.WriteChatCompletion(input);
            }
        }

        private async Task<FunctionResult> InvokeByPlannerAsync(Kernel kernel, string input, CancellationToken cancellationToken = default)
        {
#pragma warning disable SKEXP0060
            var planner = new HandlebarsPlanner();
#pragma warning restore SKEXP0060
            try
            {
#pragma warning disable SKEXP0060
                var plan = await planner.CreatePlanAsync(kernel, input);
                var executionResult = await plan.InvokeAsync(kernel);
                var promptTemplate = _promptTemplateService.LoadTemplate("AgentPrompt.txt");
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
                var promptTemplate = _promptTemplateService.LoadTemplate("AgentPrompt.txt");
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

        private async Task<IAsyncEnumerable<StreamingChatMessageContent>> InvokeStreamingByStepwisePlannerAsync(Kernel kernel, string input, CancellationToken cancellationToken = default)
        {
            try
            {
                var planner = new StepwisePlanner(_kernel, _promptTemplateService);
                planner.AddVariable("appId", _app.Id);
                planner.AddVariable("conversationId", _conversationId);

                var plan = await planner.CreatePlanAsync(input);
                var result = await plan.ExecuteAsync(_kernel,cancellationToken);
                return result.AsStreamming();
            }
            catch (Exception ex)
            {
                return Constants.DefaultErrorAnswer.AsStreamming();
            }

        }

        private async Task<FunctionResult> InvokeByKernelAsync(Kernel kernel, string input, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_app.Prompt))
                _app.Prompt = _defaultPrompt;

            var temperature = _app.Temperature / 100;
            var executionSettings = new OpenAIPromptExecutionSettings() { Temperature = (double)temperature };

            var histories = await GetHistoricalMessages(_app.Id, _conversationId, _app.MaxMessageRounds);

            _promptTemplate.AddVariable("input", input);
            _promptTemplate.AddVariable("system", _app.Prompt);
            _promptTemplate.AddVariable("histories", histories);
            return await _promptTemplate.InvokeAsync(kernel, executionSettings, cancellationToken);
        }

        private async Task<IAsyncEnumerable<StreamingChatMessageContent>> InvokeStreamingByKernelAsync(Kernel kernel, string input, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_app.Prompt))
                _app.Prompt = _defaultPrompt;

            var temperature = _app.Temperature / 100;
            var executionSettings = new OpenAIPromptExecutionSettings() { Temperature = (double)temperature };

            var histories = await GetHistoricalMessages(_app.Id, _conversationId, _app.MaxMessageRounds);


            _promptTemplate.AddVariable("input", input);
            _promptTemplate.AddVariable("system", _app.Prompt);
            _promptTemplate.AddVariable("histories", histories);
            return _promptTemplate.InvokeStreamingAsync(kernel, executionSettings, cancellationToken);
        }

        private async Task RemoveLastChatMessage(long appId, string conversationId)
        {
            var messageList = await _chatHistoriesService.GetConversationMessages(appId, conversationId);
            messageList = messageList.OrderBy(x => x.CreatedAt).ToList();
            var lastMessage = messageList.LastOrDefault();
            if (!lastMessage.IsUserMessage)
                await _chatHistoriesService.DeleteConversationMessage(lastMessage.Id);
        }
    }
}
