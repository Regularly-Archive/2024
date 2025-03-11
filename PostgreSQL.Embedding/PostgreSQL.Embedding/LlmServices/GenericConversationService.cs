using Microsoft.Extensions.Logging;
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
        private readonly HttpContext _httpContext;
        private readonly SSEEmitter _sseEmitter;
        private readonly ILogger<GenericConversationService> _logger;
        public GenericConversationService(Kernel kernel, LlmApp app, IServiceProvider serviceProvider, IChatHistoriesService chatHistoriesService, HttpContext httpContext)
            : base(kernel, chatHistoriesService)
        {
            _kernel = kernel;
            _app = app;
            _serviceProvider = serviceProvider;
            _promptTemplateService = _serviceProvider.GetService<PromptTemplateService>();
            _promptTemplate = _promptTemplateService.LoadTemplate("Default.txt");
            _chatHistoriesService = chatHistoriesService;
            _userInfoService = _serviceProvider.GetService<IUserInfoService>();
            _logger = _serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<GenericConversationService>();
            _httpContext = httpContext;
            _sseEmitter = new SSEEmitter(_httpContext);
        }

        public async Task InvokeAsync(ConversationRequestModel model, string input, CancellationToken cancellationToken = default)
        {
            _conversationId = !string.IsNullOrEmpty(model.ConversationId) 
                ? model.ConversationId 
                : Guid.NewGuid().ToString("N");

            var conversationName = _httpContext.GetConversationName();

            // 如果是重新生成，则删除最后一条 AI 消息
            var conversationFlag = _httpContext.GetConversationFlag();
            if (!conversationFlag)
            {
                _messageReferenceId = await _chatHistoriesService.AddUserMessageAsync(_app.Id, _conversationId, input);
                await _chatHistoriesService.AddConversationAsync(_app.Id, _conversationId, conversationName);
                _httpContext.Response.Headers[Constants.HttpResponseHeader_ReferenceMessageId] = _messageReferenceId.ToString();
            }
            else
            {
                // Todo: 考虑为消息增加状态，这样可以查看同一条消息的不同生成结果
                await RemoveLastChatMessage(_app.Id, _conversationId);
            }

            await _httpContext.Response.Body.FlushAsync().ConfigureAwait(false);
            var conversationTask = model.Stream
                ? InvokeStreamingChat(model, input, cancellationToken)
                : InvokeChat(_httpContext, input, cancellationToken);

            await conversationTask;
        }

        /// <summary>
        /// 流式聊天
        /// </summary>
        /// <param name="HttpContext"></param>
        /// <param name="result"></param>
        /// <param name="input"></param>
        /// <returns></returns>
        private async Task InvokeStreamingChat(ConversationRequestModel model, string input, CancellationToken cancellationToken = default)
        {
            var chatResult = model.AgenticMode
                ? await InvokeStreamingByStepwisePlannerAsync(_kernel, input, cancellationToken)
                : await InvokeStreamingByKernelAsync(_kernel, input, cancellationToken);

            var answerBuilder = new StringBuilder();
            await foreach (var content in chatResult)
            {
                if (!string.IsNullOrEmpty(content.Content)) answerBuilder.Append(content.Content);
            }

            var messageId = await _chatHistoriesService.AddSystemMessageAsync(_app.Id, _conversationId, answerBuilder.ToString());
            //HttpContext.Response.Headers[Constants.HttpResponseHeader_ReferenceMessageId] = _messageReferenceId.ToString();
            await _httpContext.WriteStreamingChatCompletion(chatResult, messageId, cancellationToken);
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
                _logger.LogError($"An error occurs when execute a plan due to {ex.Message}");
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
                planner.AddVariable("currentTime", DateTime.Now);

                var plan = await planner.CreatePlanAsync(input);
                plan.OnStepExecute = async trace => await EmitTracesAsync(trace, cancellationToken);
                var result = await plan.ExecuteAsync(_kernel, cancellationToken);

                return result.AsStreaming();
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurs when execute a plan due to {ex.Message}");
                return Constants.DefaultErrorAnswer.AsStreaming();
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

            var histories = await SearchHistoricalMessagesAsync(_app.Id, _conversationId, input, _app.MaxMessageRounds);

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

        /// <summary>
        /// 通过 SSE 发送日志信息
        /// </summary>
        /// <param name="text"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task EmitTracesAsync(string text, CancellationToken cancellationToken = default)
        {
            var result = new OpenAIStreamResult() { id = Guid.NewGuid().ToString("N"), obj = "chat.traces" };
            result.choices.Add(new StreamChoicesModel() { delta = new OpenAIMessage() { role = "assistant", content = text } });
            await _sseEmitter.EmitAsync(result, cancellationToken);
        }
    }
}
