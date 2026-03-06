using Masuit.Tools;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Planning.Handlebars;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Domain.Models.Planners;
using PostgreSQL.Embedding.Infrastructure.UserIdentity;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Core.ChatHistory.Services;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Llm.Services;
using PostgreSQL.Embedding.Utils;
using System.Diagnostics;
using System.Text;


namespace PostgreSQL.Embedding.Llm.Core
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
        private readonly ICurrentUserService _currentUserService;
        private readonly HttpContext _httpContext;
        private readonly SSEEmitter _sseEmitter;
        private readonly ILogger<GenericConversationService> _logger;
        private readonly AgentExecutionContext _agentExecutionContext;
        private readonly CitationService _citationService;
        public GenericConversationService(
            Kernel kernel,
            LlmApp app,
            IServiceProvider serviceProvider,
            IChatHistoriesService chatHistoriesService,
            HttpContext httpContext,
            ChatHistoryManager? chatHistoryManager = null)
            : base(kernel, chatHistoriesService, serviceProvider)
        {
            _kernel = kernel;
            _app = app;
            _serviceProvider = serviceProvider;
            _promptTemplateService = _serviceProvider.GetService<PromptTemplateService>();
            _promptTemplate = _promptTemplateService.LoadTemplate("Default.txt");
            _chatHistoriesService = chatHistoriesService;
            _currentUserService = _serviceProvider.GetService<ICurrentUserService>();
            _logger = _serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<GenericConversationService>();
            _httpContext = httpContext;
            _sseEmitter = new SSEEmitter(_httpContext);
            _agentExecutionContext = _kernel.GetAgentExecutionContext();
            _citationService = _serviceProvider.GetRequiredService<CitationService>();
        }

        public async Task InvokeAsync(ConversationRequestModel conversationRequest, string input, CancellationToken cancellationToken = default)
        {
            _conversationId = !string.IsNullOrEmpty(conversationRequest.ConversationId) ? conversationRequest.ConversationId : Guid.NewGuid().ToString("N");
            var conversationName = _httpContext.GetConversationName();

            _agentExecutionContext.SetAppId(_app.Id);
            _agentExecutionContext.SetConversationId(_conversationId);

            // 如果是重新生成，则删除最后一条 AI 消息
            var conversationFlag = _httpContext.GetConversationFlag();
            if (!conversationFlag)
            {
                _messageReferenceId = await _chatHistoriesService.AddUserMessageAsync(_app.Id, _conversationId, input);
                _httpContext.Response.Headers[Constants.HttpResponseHeader_ReferenceMessageId] = _messageReferenceId.ToString();
                _agentExecutionContext.SetReferenceMessageId(_messageReferenceId);

                var conversation = await _chatHistoriesService.GetAppConversationAsync(_app.Id, _conversationId);
                if (conversation == null)
                {
                    var conversationSummary = await GenerateConversationTitle(input);
                    await _chatHistoriesService.AddConversationAsync(_app.Id, _conversationId, conversationSummary);
                    await EmitConversationTitleAsync(_messageReferenceId, conversationSummary);
                    await _chatHistoriesService.UpdateConversationAsync(_app.Id, _conversationId, conversationSummary);
                }
            }
            else
            {
                // Todo: 考虑为消息增加状态，这样可以查看同一条消息的不同生成结果
                await RemoveLastChatMessage(_app.Id, _conversationId);
            }

            await _httpContext.Response.Body.FlushAsync().ConfigureAwait(false);
            var conversationTask = conversationRequest.Stream
                ? InvokeStreamingChat(conversationRequest, input, cancellationToken)
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
        private async Task InvokeStreamingChat(ConversationRequestModel conversationRequest, string input, CancellationToken cancellationToken = default)
        {
            var messageId = await _chatHistoriesService.AddSystemMessageAsync(_app.Id, _conversationId, string.Empty);
            _agentExecutionContext.SetMessageId(messageId);

            var chatResult = conversationRequest.AgenticMode
                ? await InvokeStreamingByStepwisePlannerAsync(_kernel, conversationRequest, input, cancellationToken)
                : await InvokeStreamingByKernelAsync(_kernel, input, cancellationToken);

            var answerBuilder = new StringBuilder();
            await foreach (var content in chatResult)
            {
                if (!string.IsNullOrEmpty(content.Content)) answerBuilder.Append(content.Content);
            }

            //HttpContext.Response.Headers[Constants.HttpResponseHeader_ReferenceMessageId] = _messageReferenceId.ToString();
            var answerWithoutCitationsTag = answerBuilder.ToString().Replace("<CITATIONS>", "").Replace("</CITATIONS>", "");
            await _httpContext.WriteStreamingChatCompletion(chatResult, messageId, cancellationToken);
            await _chatHistoriesService.UpdateSystemMessageAsync(messageId, answerWithoutCitationsTag);
        }

        /// <summary>
        /// 普通聊天
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private async Task InvokeChat(HttpContext HttpContext, string input, CancellationToken cancellationToken = default)
        {
            var usePlugin = false;
            var messageId = await _chatHistoriesService.AddSystemMessageAsync(_app.Id, _conversationId, string.Empty);

            var chatResult = usePlugin
                ? await InvokeByPlannerAsync(_kernel, input)
                : await InvokeByKernelAsync(_kernel, input);

            var answer = chatResult.GetValue<string>();
            if (!string.IsNullOrEmpty(answer))
            {
                await HttpContext.WriteChatCompletion(answer, messageId);
                await _chatHistoriesService.UpdateSystemMessageAsync(messageId, answer);
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

        private async Task<IAsyncEnumerable<StreamingChatMessageContent>> InvokeStreamingByStepwisePlannerAsync(Kernel kernel, ConversationRequestModel conversationRequest, string input, CancellationToken cancellationToken = default)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var currentUser = await _currentUserService.GetCurrentUserAsync();
                //var chatHistory = await GetHistoricalMessagesAsync(_app.Id, _conversationId, _app.MaxMessageRounds);

                var runId = Guid.NewGuid().ToString();
                _agentExecutionContext.SetRunId(runId);

                var taskPlanner = new TaskPlanner(kernel);
                var planResult = _app.AppType == (int)LlmAppType.Chat
                    ? await taskPlanner.GetSubTasksAsync(input, limit: 3)
                    : await taskPlanner.GetRAGTasks(input);

                //await EmitTracesAsync(StepTrace.PlanningDone(_agentExecutionContext.GetMessageId()));

                var subTasks = planResult.Tasks;
                if (!subTasks.Any()) return Constants.DefaultErrorAnswer.AsStreaming();

                await UpdateReasoningContent(_agentExecutionContext.GetMessageId(), planResult.Thought);

                foreach (var stepTrace in StepTrace.AsStreamingThought(input, planResult.Thought, "", _agentExecutionContext.GetMessageId()))
                {
                    await Task.Delay(100);
                    await EmitTracesAsync(stepTrace);
                }

                subTasks.ForEach(async (subTask) => await EmitTracesAsync(subTask.AsStepTrace(_agentExecutionContext.GetMessageId()), cancellationToken));

                var planner = new StepwisePlanner(_kernel, _promptTemplateService, new StepwisePlannerConfig() { MaxIterations = 30 });
                planner.AddVariable("appId", _app.Id);
                planner.AddVariable("runId", runId);
                planner.AddVariable("conversationId", _conversationId);
                planner.AddVariable("userId", currentUser.Id);
                planner.AddVariable("currentTime", DateTime.Now);
                planner.AddVariable("enableWebSearch", conversationRequest.AccessInternet);
                planner.AddVariable("skillsRootFolder", "D:\\Projects\\skills\\skills");
                planner.AddVariable("EnableMCP", false);
                planner.AddVariable("EnableSkills", true);

                var graphExecutor = new DAGraphExecutor(input, subTasks, planner, kernel, _citationService);
                var reasoningContent = string.Empty;
                graphExecutor.OnStepChanged = async (stepTrace) =>
                {
                    if (stepTrace.Type == "Thought") reasoningContent += stepTrace.Content;
                    await EmitTracesAsync(stepTrace);
                };
                await graphExecutor.ExecuteAsync(cancellationToken);

                stopwatch.Stop();
                await UpdateReasoningContent(_agentExecutionContext.GetMessageId(), reasoningContent);
                _logger.LogInformation($"本次任务耗时 {stopwatch.Elapsed.TotalSeconds.Round(2)} 秒");
                await EmitTracesAsync(StepTrace.ThinkDone(_agentExecutionContext.GetMessageId(), stopwatch.Elapsed.TotalSeconds));
                return subTasks.OrderByDescending(x => x.Id).FirstOrDefault().ExecuteResult.AsStreaming();
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

            var referenceMessage = messageList.LastOrDefault(x => x.IsUserMessage);
            if (referenceMessage == null) return;

            _messageReferenceId = referenceMessage.Id;

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
        private async Task EmitTracesAsync(StepTrace stepTrace, CancellationToken cancellationToken = default)
        {
            var result = new OpenAIStreamResult() { id = Guid.NewGuid().ToString("N"), obj = "chat.traces" };
            result.choices.Add(new StreamChoicesModel() { delta = new OpenAIMessage() { role = "assistant", content = JsonConvert.SerializeObject(stepTrace) } });
            await _sseEmitter.EmitAsync(result, cancellationToken);
        }

        private async Task EmitConversationTitleAsync(long? messageId, string conversationTitle, CancellationToken cancellationToken = default)
        {
            var result = new OpenAIStreamResult() { id = messageId.HasValue ? messageId.ToString() : Guid.NewGuid().ToString(), obj = "conversation.title" };
            result.choices = new List<StreamChoicesModel>()
            {
                new StreamChoicesModel() { delta = new OpenAIMessage() { role = "assistant" } }
            };

            result.choices[0].delta.content = conversationTitle;
            await _sseEmitter.EmitAsync(result, cancellationToken);
        }

        private async Task UpdateReasoningContent(long messageId, string reasoningContent)
        {
            await _chatHistoriesService.UpdateSystemMessageAsync(messageId, message =>
            {
                var newContent = (message.ReasoningContent ?? "") + reasoningContent;
                message.ReasoningContent = newContent;
            });
        }
    }
}
