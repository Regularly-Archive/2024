using LLama.Batched;
using Masuit.Tools;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Common.Streaming;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Domain.Models.Planners;
using PostgreSQL.Embedding.Infrastructure.UserIdentity;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Llm.Services;
using PostgreSQL.Embedding.Utils;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using TaskState = PostgreSQL.Embedding.Domain.Models.Planners.TaskState;


namespace PostgreSQL.Embedding.Llm.Core
{
    /// <summary>
    /// Helper class to track SSE block index for content blocks
    /// </summary>
    public class SseBlockTracker
    {
        public int ThinkingBlockIndex { get; private set; }
        public int TextBlockIndex { get; private set; }

        public void InitializeThinkingBlock() => ThinkingBlockIndex = ThinkingBlockIndex + 1;
        public void InitializeTextBlock() => TextBlockIndex = 0;
    }

    /// <summary>
    /// Agentic conversation service that streams SSE events following Anthropic format.
    /// Uses Channel for efficient multi-producer event streaming.
    /// Integrates StepTrace for planning, reasoning, and tool execution events.
    /// </summary>
    public class AgenticConversationService : BaseConversationService
    {
        private readonly Kernel _kernel;
        private readonly LlmApp _app;
        private readonly IServiceProvider _serviceProvider;
        private readonly PromptTemplateService _promptTemplateService;
        private readonly IChatHistoriesService _chatHistoriesService;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<AgenticConversationService> _logger;
        private readonly AgentExecutionContext _agentExecutionContext;
        private readonly string _defaultPrompt = "You are a helpful AI bot. You must answer the question in Chinese.";

        public AgenticConversationService(
            Kernel kernel,
            LlmApp app,
            IServiceProvider serviceProvider,
            IChatHistoriesService chatHistoriesService)
            : base(kernel, chatHistoriesService)
        {
            _kernel = kernel;
            _app = app;
            _serviceProvider = serviceProvider;
            _chatHistoriesService = chatHistoriesService;
            _promptTemplateService = serviceProvider.GetService<PromptTemplateService>()!;
            _currentUserService = serviceProvider.GetService<ICurrentUserService>()!;
            _logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<AgenticConversationService>();
            _agentExecutionContext = kernel.GetAgentExecutionContext();
        }

        /// <summary>
        /// Main entry point - returns an async enumerable of SSE events following Anthropic format.
        /// </summary>
        public async IAsyncEnumerable<ISseEvent> InvokeAsync(
            ConversationRequestModel request,
            string input,
            string? conversationId = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            // Setup conversation context
            var convId = string.IsNullOrEmpty(conversationId) ? Guid.NewGuid().ToString("N") : conversationId;
            var runId = Guid.NewGuid().ToString();
            _agentExecutionContext.SetRunId(runId);
            _agentExecutionContext.SetAppId(_app.Id);
            _agentExecutionContext.SetConversationId(convId);
            _agentExecutionContext.InitializeSandboxContext(_app.Id, convId, runId);

            // Add user message
            var refMessageId = await _chatHistoriesService.AddUserMessageAsync(_app.Id, convId, input);
            _agentExecutionContext.SetReferenceMessageId(refMessageId);

            // Add app conversation
            var conversationTitle = string.Empty;
            var conversation = await _chatHistoriesService.GetAppConversationAsync(_app.Id, convId);
            if (conversation == null)
            {
                conversationTitle = await GenerateConversationTitle(input);
                await _chatHistoriesService.AddConversationAsync(_app.Id, convId, conversationTitle);
            }
            else
            {
                conversationTitle = conversation.Title;
            }

            // Add system message
            var messageId = await _chatHistoriesService.AddSystemMessageAsync(_app.Id, convId, string.Empty);
            _agentExecutionContext.SetMessageId(messageId);

            var metadata = new ConversationContext { ConversationId = convId, ConversationTitle = conversationTitle, ReferenceMessageId = refMessageId.ToString() };

            // Create channel for event streaming
            var channel = Channel.CreateUnbounded<ISseEvent>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true
            });

            // Initialize EventBus for plugin event publishing
            _agentExecutionContext.InitializeEventBus(channel.Writer);

            // Start event production in background
            _ = ProduceEventsAsync(request, input, metadata, messageId, channel.Writer, ct);

            // Consume and yield events to the caller
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            {
                yield return evt;
            }
        }

        private async Task ProduceEventsAsync(
            ConversationRequestModel request,
            string input,
            ConversationContext conversationContext,
            long messageId,
            ChannelWriter<ISseEvent> writer,
            CancellationToken ct)
        {
            var conversationId = conversationContext.ConversationId;

            // Initialize block tracker for thinking and text blocks
            var blockTracker = new SseBlockTracker();
            //blockTracker.InitializeThinkingBlock(); // index 0
            //blockTracker.InitializeTextBlock();     // index 0

            try
            {
                // 1. message_start - indicates message begins
                await writer.WriteAsync(new MessageStartEvent
                {
                    Message = new MessageMetadata
                    {
                        Id = messageId.ToString(),
                        Role = "assistant",
                        Content = new(),
                        Model = "",
                        Context = conversationContext
                    }
                }, ct);

                // 2. ping - heartbeat to keep connection alive
                await writer.WriteAsync(new PingEvent(), ct);

                // 3. Planning phase - emit planning events
                var subtasks = await EmitPlanningEventsAsync(request, input, conversationId, messageId, writer, blockTracker, ct);

                // 4. Execute subtasks and emit tool/action events
                var finalResult = await ExecuteSubTasksAsync(request, input, conversationId, messageId, writer, blockTracker, subtasks.ToList(), ct);

                // 5. Generate final response with text block (thinking already emitted during planning/execution)
                await EmitFinalResponseAsync(input, conversationId, messageId, finalResult, writer, blockTracker.TextBlockIndex, ct);

                // 5. message_delta - final message state with usage
                await writer.WriteAsync(new MessageDeltaEvent
                {
                    Delta = new MessageDelta { StopReason = "end_turn" },
                    Usage = new UsageInfo
                    {
                        InputTokens = EstimateTokenCount(input),
                        OutputTokens = EstimateTokenCount(finalResult ?? "")
                    }
                }, ct);

                // 6. message_stop - message complete
                await writer.WriteAsync(new MessageStopEvent(), ct);

                // Save assistant response to history
                if (!string.IsNullOrEmpty(finalResult))
                {
                    await _chatHistoriesService.UpdateSystemMessageAsync(messageId, finalResult);
                }

                writer.Complete();
            }
            catch (OperationCanceledException)
            {
                // Client disconnected gracefully
                writer.Complete();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error producing events");
                await writer.WriteAsync(new ErrorEvent
                {
                    Error = new ErrorDetails { ErrorType = "internal_error", Message = ex.Message }
                }, ct);
                writer.Complete(ex);
            }
        }

        private async Task<IEnumerable<SubTask>> EmitPlanningEventsAsync(
            ConversationRequestModel request,
            string input,
            string conversationId,
            long messageId,
            ChannelWriter<ISseEvent> writer,
            SseBlockTracker blockTracker,
            CancellationToken ct)
        {
            var currentUser = await _currentUserService.GetCurrentUserAsync();

            // Create task planner
            var taskPlanner = new TaskPlanner(_kernel);
            var planResult = _app.AppType == (int)LlmAppType.Chat
                ? await taskPlanner.GetSubTasksAsync(input, limit: 3)
                : await taskPlanner.GetRAGTasks(input);

            var subTasks = planResult.Tasks;
            if (!subTasks.Any())
            {
                await writer.WriteAsync(new ErrorEvent
                {
                    Error = new ErrorDetails { ErrorType = "planning_error", Message = "No tasks generated" }
                }, ct);
                return [];
            }

            //blockTracker.InitializeThinkingBlock();
            // Emit planning thought as content_block (thinking) - uses thinking block index
            _logger.LogInformation($"[THOUGHT] {planResult.Thought}");
            await EmitThinkingBlockAsync(planResult.Thought, writer, blockTracker.ThinkingBlockIndex, ct);

            // Emit each subtask as a planning event
            foreach (var subTask in subTasks)
            {
                await writer.WriteAsync(new PlanningEvent
                {
                    Id = subTask.Id.ToString(),
                    Title = subTask.Name,
                    Description = subTask.Description,
                    Status = "pending"
                }, ct);
            }

            return subTasks;
        }

        private async Task<string> ExecuteSubTasksAsync(
            ConversationRequestModel request,
            string input,
            string conversationId,
            long messageId,
            ChannelWriter<ISseEvent> writer,
            SseBlockTracker blockTracker,
            List<SubTask> subTasks,
            CancellationToken ct)
        {
            var currentUser = await _currentUserService.GetCurrentUserAsync();
            var runId = _agentExecutionContext.GetRunId();

            // Re-create task planner to get subtasks

            // Create planner for task execution
            var planner = new StepwisePlanner(_kernel, _promptTemplateService, new StepwisePlannerConfig { MaxIterations = 30 });
            planner.AddVariable("appId", _app.Id);
            planner.AddVariable("runId", runId);
            planner.AddVariable("conversationId", conversationId);
            planner.AddVariable("userId", currentUser.Id);
            planner.AddVariable("currentTime", DateTime.Now);
            planner.AddVariable("enableWebSearch", request.AccessInternet);
            planner.AddVariable("skillsRootFolder", "C:\\Users\\Administrator\\.claude\\skills");
            planner.AddVariable("EnableMCP", true);
            planner.AddVariable("EnableSkills", true);

            // Create DAG executor
            var graphExecutor = new DAGraphExecutor(input, subTasks, planner, _kernel);

            // Hook into step changes to emit events
            graphExecutor.OnStepChanged = async (stepTrace) =>
            {
                await EmitStepTraceAsync(stepTrace, writer, blockTracker, ct);
            };

            await graphExecutor.ExecuteAsync();

            // Return the result from the last completed subtask
            var completedTask = subTasks.OrderByDescending(x => x.Id).FirstOrDefault(x => x.State == TaskState.Completed);
            return completedTask?.ExecuteResult ?? "";
        }

        private async Task EmitStepTraceAsync(StepTrace stepTrace, ChannelWriter<ISseEvent> writer, SseBlockTracker blockTracker, CancellationToken ct)
        {
            switch (stepTrace.Type)
            {
                case "Thought":
                    // Emit as content_block (thinking) format - uses thinking block index
                    blockTracker.InitializeThinkingBlock();
                    await EmitThinkingBlockAsync(stepTrace.Content, writer, blockTracker.ThinkingBlockIndex, ct);
                    break;

                case "Action":
                    // Emit as tool call events with input/output
                    var actionObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(stepTrace.Content);
                    var input = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(actionObj["input"])
                     );
                    await writer.WriteAsync(new ToolCallEvent
                    {
                        Id = stepTrace.Id,
                        Name = ExtractActionName(stepTrace.Title),
                        Input = input,
                        Output = JsonConvert.SerializeObject(actionObj["output"]),
                        Status = stepTrace.Status == "success" ? "completed" : "error",
                        DurationMs = (long)(ExtractDuration(stepTrace.Description) * 1000)
                    }, ct);
                    break;

                case "Plan":
                    // Emit as planning events with updated status
                    await writer.WriteAsync(new PlanningEvent
                    {
                        Id = stepTrace.Id,
                        Title = stepTrace.Title,
                        Description = stepTrace.Description,
                        Content = stepTrace.Content,
                        Status = MapPlanStatus(stepTrace.Status)
                    }, ct);
                    break;

                case "MessageStatus":
                    // Could emit as system messages if needed
                    break;
            }
        }

        /// <summary>
        /// Emit thinking content in Anthropic's content_block format.
        /// Format: content_block_start (thinking) -> content_block_delta (thinking_delta) -> content_block_stop
        /// </summary>
        private async Task EmitThinkingBlockAsync(string thinking, ChannelWriter<ISseEvent> writer, int blockIndex, CancellationToken ct)
        {
            //if (string.IsNullOrEmpty(thinking))
            //{
            //    // Still emit empty thinking block to maintain block structure
            //    await writer.WriteAsync(new ContentBlockStartEvent
            //    {
            //        Index = blockIndex,
            //        ContentBlock = new ContentBlock { BlockType = "thinking", Thinking = "" }
            //    }, ct);
            //    await writer.WriteAsync(new ContentBlockStopEvent { Index = blockIndex }, ct);
            //    return;
            //}
            // content_block_start - thinking block
            await writer.WriteAsync(new ContentBlockStartEvent
            {
                Index = blockIndex,
                ContentBlock = new ContentBlock { BlockType = "thinking", Thinking = "" }
            }, ct);

            // content_block_delta - thinking_delta chunks
            foreach (var chunk in SplitText(thinking, 50))
            {
                await writer.WriteAsync(new ContentBlockDeltaEvent
                {
                    Index = blockIndex,
                    Delta = new ContentBlockDelta { DeltaType = "thinking_delta", Thinking = chunk }
                }, ct);
                await Task.Delay(20, ct);
            }

            // content_block_stop - thinking complete
            await writer.WriteAsync(new ContentBlockStopEvent { Index = blockIndex }, ct);
        }

        private async Task EmitFinalResponseAsync(
            string input,
            string conversationId,
            long messageId,
            string result,
            ChannelWriter<ISseEvent> writer,
            int blockIndex,
            CancellationToken ct)
        {
            // content_block_start - text response block
            await writer.WriteAsync(new ContentBlockStartEvent
            {
                Index = blockIndex,
                ContentBlock = new ContentBlock { BlockType = "text" }
            }, ct);

            // Emit final response in chunks
            if (!string.IsNullOrEmpty(result))
            {
                foreach (var chunk in SplitText(result, 50))
                {
                    await writer.WriteAsync(new ContentBlockDeltaEvent
                    {
                        Index = blockIndex,
                        Delta = new ContentBlockDelta { DeltaType = "text_delta", Text = chunk }
                    }, ct);
                    await Task.Delay(20, ct);
                }
            }

            // content_block_stop
            await writer.WriteAsync(new ContentBlockStopEvent { Index = blockIndex }, ct);
        }

        private static string[] SplitText(string text, int chunkSize)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();

            var chunks = new List<string>();
            for (int i = 0; i < text.Length; i += chunkSize)
            {
                chunks.Add(text.Substring(i, Math.Min(chunkSize, text.Length - i)));
            }
            return chunks.ToArray();
        }
        private static string ExtractActionName(string title)
        {
            var actionName = title.Split('|', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            return actionName.Replace("Plugin", "");
        }
        private static double ExtractDuration(string description)
        {
            var match = System.Text.RegularExpressions.Regex.Match(description, @"耗时\s+([\d.]+)\s+秒");
            return match.Success ? double.Parse(match.Groups[1].Value) : 0;
        }

        private static string MapPlanStatus(string status)
        {
            return status?.ToLower() switch
            {
                "pending" => "pending",
                "inprogress" => "inprogress",
                "completed" => "completed",
                "failed" => "failed",
                _ => "pending"
            };
        }

        private static int EstimateTokenCount(string text)
        {
            return (int)Math.Ceiling((text.Length / 4.0));
        }
    }
}
