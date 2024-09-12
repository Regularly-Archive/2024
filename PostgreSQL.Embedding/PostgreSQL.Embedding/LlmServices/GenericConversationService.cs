using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Planning.Handlebars;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.LLmServices.Extensions;
using PostgreSQL.Embedding.Planners;
using PostgreSQL.Embedding.Services;
using PostgreSQL.Embedding.Utils;
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
        private long _messageReferenceId;
        private readonly Random _random = new Random();
        private readonly IUserInfoService _userInfoService;
        public GenericConversationService(Kernel kernel, LlmApp app, IServiceProvider serviceProvider, IChatHistoriesService chatHistoriesService)
            : base(kernel, chatHistoriesService)
        {
            _kernel = kernel;
            _app = app;
            _serviceProvider = serviceProvider;
            _promptTemplateService = _serviceProvider.GetService<PromptTemplateService>();
            _promptTemplate = _promptTemplateService.LoadTemplate("Default.txt");
            _chatHistoriesService = chatHistoriesService;
            _userInfoService = _serviceProvider.GetService<IUserInfoService>();
        }

        public async Task InvokeAsync(OpenAIModel model, HttpContext httpContext, string input, CancellationToken cancellationToken = default)
        {
            _conversationId = httpContext.GetOrCreateConversationId();
            var conversationName = httpContext.GetConversationName();

            // 如果是重新生成，则删除最后一条 AI 消息
            var conversationFlag = httpContext.GetConversationFlag();
            if (!conversationFlag)
            {
                _messageReferenceId = await _chatHistoriesService.AddUserMessageAsync(_app.Id, _conversationId, input);
                await _chatHistoriesService.AddConversationAsync(_app.Id, _conversationId, conversationName);
            }
            else
            {
                // Todo: 考虑为消息增加状态，这样可以查看同一条消息的不同生成结果
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

            var messageId = await _chatHistoriesService.AddSystemMessageAsync(_app.Id, _conversationId, answerBuilder.ToString());
            HttpContext.Response.Headers[Constants.HttpResponseHeader_ReferenceMessageId] = _messageReferenceId.ToString();
            await HttpContext.WriteStreamingChatCompletion(chatResult, messageId, cancellationToken);
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
                var messageId = await _chatHistoriesService.AddSystemMessageAsync(_app.Id, _conversationId, answer);
                HttpContext.Response.Headers[Constants.HttpResponseHeader_ReferenceMessageId] = _messageReferenceId.ToString();
                await HttpContext.WriteChatCompletion(input, messageId);
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

        private async Task<IAsyncEnumerable<StreamingChatMessageContent>> InvokeStreamingByStepwisePlannerAsync(Kernel kernel, string input, CancellationToken cancellationToken = default)
        {
            try
            {
                var currentUser = await _userInfoService.GetCurrentUserAsync();
                var planner = new StepwisePlanner(_kernel, _promptTemplateService);
                planner.AddVariable("appId", _app.Id);
                planner.AddVariable("conversationId", _conversationId);
                planner.AddVariable("userId", currentUser.Id);

                var plan = await planner.CreatePlanAsync(input);
                var result = await plan.ExecuteAsync(_kernel, cancellationToken);
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

            var histories = await GetHistoricalMessagesAsync(_app.Id, _conversationId, _app.MaxMessageRounds);

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

            var histories = await GetHistoricalMessagesAsync(_app.Id, _conversationId, _app.MaxMessageRounds);


            _promptTemplate.AddVariable("input", input);
            _promptTemplate.AddVariable("system", _app.Prompt);
            _promptTemplate.AddVariable("histories", histories);
            return _promptTemplate.InvokeStreamingAsync(kernel, executionSettings, cancellationToken);
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
