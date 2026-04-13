using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Common.Streaming;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Domain.Models.Planners;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Infrastructure.Sandbox;
using PostgreSQL.Embedding.Infrastructure.UserIdentity;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Llm.Services;
using System.Runtime.CompilerServices;
using System.Text;
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
        private readonly IOptions<SandboxOptions> _sandboxOptions;
        private readonly CitationService _citationService;

        // Database operation lock to prevent concurrent access issues
        private readonly SemaphoreSlim _dbLock = new(1, 1);

        // Repositories for trace persistence (lazy loaded)
        private IRepository<ChatMessageReasoning>? _reasoningRepository;
        private IRepository<ChatMessageToolCall>? _toolCallRepository;
        private IRepository<ChatMessagePlan>? _planRepository;
        private IRepository<AgentRun>? _agentRunsRepository;

        public AgenticConversationService(
            Kernel kernel,
            LlmApp app,
            IServiceProvider serviceProvider,
            IChatHistoriesService chatHistoriesService)
            : base(kernel, chatHistoriesService, serviceProvider)
        {
            _kernel = kernel;
            _app = app;
            _serviceProvider = serviceProvider;
            _chatHistoriesService = chatHistoriesService;
            _promptTemplateService = serviceProvider.GetService<PromptTemplateService>()!;
            _currentUserService = serviceProvider.GetService<ICurrentUserService>()!;
            _logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<AgenticConversationService>();
            _agentExecutionContext = kernel.GetAgentExecutionContext();
            _sandboxOptions = serviceProvider.GetService<IOptions<SandboxOptions>>()!;
            _citationService = serviceProvider.GetRequiredService<CitationService>();
            _reasoningRepository = _serviceProvider.GetRequiredService<IRepository<ChatMessageReasoning>>();
            _toolCallRepository = _serviceProvider.GetRequiredService<IRepository<ChatMessageToolCall>>();
            _planRepository = _serviceProvider.GetRequiredService<IRepository<ChatMessagePlan>>();
            _agentRunsRepository = _serviceProvider.GetRequiredService<IRepository<AgentRun>>();
        }

        /// <summary>
        /// Main entry point - returns an async enumerable of SSE events following Anthropic format.
        /// </summary>
        public async IAsyncEnumerable<ISseEvent> InvokeAsync(
            string input,
            string? conversationId = null,
            string? runId = null,
            IEnumerable<UserInputFile> userInputFiles = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            // Setup conversation context
            var convId = string.IsNullOrEmpty(conversationId) ? Guid.NewGuid().ToString("N") : conversationId;

            if (string.IsNullOrEmpty(runId)) runId = Guid.NewGuid().ToString("N");

            _agentExecutionContext.SetRunId(runId);
            _agentExecutionContext.SetAppId(_app.Id);
            _agentExecutionContext.SetConversationId(convId);
            _agentExecutionContext.InitializeSandboxContext(_app.Id, convId, runId, _sandboxOptions);
            _agentExecutionContext.SetAgentState(AgentState.Running);

            var agentRun = await _agentRunsRepository.FindAsync(x => x.RunId == runId);

            // Add user message
            var refMessageId = agentRun == null
                ? await _chatHistoriesService.AddUserMessageAsync(_app.Id, convId, input)
                : agentRun.RefMessageId;

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
            var messageId = agentRun == null
                ? await _chatHistoriesService.AddSystemMessageAsync(_app.Id, convId, string.Empty)
                : agentRun.MessageId;
            _agentExecutionContext.SetMessageId(messageId);

            if (agentRun == null)
            {
                agentRun = new AgentRun() { RunId = runId, ConversationId = convId, RefMessageId = refMessageId, MessageId = messageId };
                await _agentRunsRepository.AddAsync(agentRun);
            }

            var metadata = new ConversationContext { ConversationId = convId, ConversationTitle = conversationTitle, ReferenceMessageId = refMessageId.ToString(), RunId = runId };

            // Create channel for event streaming
            var channel = Channel.CreateUnbounded<ISseEvent>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true
            });

            // Initialize EventBus for plugin event publishing
            _agentExecutionContext.InitializeEventBus(channel.Writer);

            // Start event production in background
            _ = ProduceEventsAsync(input, metadata, messageId, channel.Writer, ct);

            // Consume and yield events to the caller
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                yield return evt;
            }
        }

        private async Task ProduceEventsAsync(
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
                var subtasks = await EmitPlanningEventsAsync(input, conversationId, messageId, writer, blockTracker, ct);
                if (!subtasks.Any()) return;

                // 4. Execute subtasks and emit tool/action events
                var finalResult = await ExecuteSubTasksAsync(input, conversationId, messageId, writer, blockTracker, subtasks.ToList(), ct);

                // 5. Generate final response with text block (thinking already emitted during planning/execution)
                await EmitFinalResponseAsync(input, conversationId, messageId, finalResult, writer, blockTracker.TextBlockIndex, ct);

                // 6. Citations phase - emit citations event
                var finalTask = subtasks.OrderByDescending(x => x.Id).FirstOrDefault();
                if (finalTask.CitationItems.Any())
                {
                    await EmitCitations(writer, finalTask.CitationItems, ct);
                }

                // 7. message_delta - final message state with usage
                await writer.WriteAsync(new MessageDeltaEvent
                {
                    Delta = new MessageDelta { StopReason = "end_turn" },
                    Usage = new UsageInfo
                    {
                        InputTokens = EstimateTokenCount(input),
                        OutputTokens = EstimateTokenCount(finalResult ?? "")
                    }
                }, ct);

                // 8. message_stop - message complete
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
            string input,
            string conversationId,
            long messageId,
            ChannelWriter<ISseEvent> writer,
            SseBlockTracker blockTracker,
            CancellationToken ct)
        {
            var currentUser = await _currentUserService.GetCurrentUserAsync();
            var taskMemory = await GetOrCreateContextAsync(_app.Id, conversationId);
            await InjectTaskMemory(taskMemory);

            // Create task planner
            var taskPlanner = new TaskPlanner(_kernel);
            var planResult = _app.AppType == (int)LlmAppType.Chat
                ? await taskPlanner.GetSubTasksAsync(query: input, history: taskMemory, limit: 10, ct)
                : await taskPlanner.GetRAGTasks(query: input, history: taskMemory, ct);

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
            await SaveReasoningAsync(planResult.Thought);

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

            // Create planner for task execution
            var planner = new StepwisePlanner(_kernel, _promptTemplateService, new StepwisePlannerConfig { MaxIterations = 30, ToolCallMode = ToolCallMode.Async });
            planner.AddVariable("appId", _app.Id);
            planner.AddVariable("runId", runId);
            planner.AddVariable("conversationId", conversationId);
            planner.AddVariable("userId", currentUser.Id);
            planner.AddVariable("currentTime", DateTime.Now);
            planner.AddVariable("enableWebSearch", true);
            planner.AddVariable("EnableMCP", true);
            planner.AddVariable("EnableSkills", true);
            planner.AddVariable("WorkDir", $"/sandbox");
            planner.AddVariable("ArtifactsDir", $"/sandbox/artifacts");


            // Create DAG executor
            var graphExecutor = new DAGraphExecutor(input, subTasks, planner, _kernel, _citationService);

            // Hook into step changes to emit events
            graphExecutor.OnStepChanged = async (stepTrace) =>
            {
                await EmitStepTraceAsync(stepTrace, writer, blockTracker, ct);
            };

            await graphExecutor.ExecuteAsync(ct);

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
                    await SaveReasoningAsync(stepTrace.Content);
                    await EmitThinkingBlockAsync(stepTrace.Content, writer, blockTracker.ThinkingBlockIndex, ct);
                    break;

                case "Action":
                    var toolCallName = ExtractActionName(stepTrace.Title);
                    if (toolCallName.IndexOf("AskUser") != -1) break;
                    // Emit as tool call events with input/output
                    var actionObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(stepTrace.Content);
                    var input = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(actionObj["input"])
                     );
                    var toolCallId = await SaveToolCallAsync(
                        toolCallName,
                        input,
                        actionObj["output"]?.ToString(),
                        stepTrace.Status == "success" ? 0 : -1,
                        (long)(ExtractDuration(stepTrace.Description) * 1000)
                    );
                    await writer.WriteAsync(new ToolCallEvent
                    {
                        Id = toolCallId.ToString(),
                        Name = toolCallName,
                        Input = input,
                        Output = actionObj["output"]?.ToString(),
                        Status = stepTrace.Status == "success" ? "completed" : "failed",
                        DurationMs = ExtractDuration(stepTrace.Description) * 1000
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
                    await SavePlanAsync(
                        int.Parse(stepTrace.Id),
                        stepTrace.Title,
                        stepTrace.Description,
                        stepTrace.Content,
                        (int)MapTaskState(stepTrace.Status)
                    );
                    break;
                case "ToolUse":
                    var toolUseName = ExtractActionName(stepTrace.Title);
                    if (toolUseName.IndexOf("AskUser") != -1) break;
                    // Emit as tool call events with input/output
                    var toolUseObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(stepTrace.Content);
                    var toolUseInput = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(toolUseObj["input"])
                     );
                    var toolUseId = await SaveToolUseAsync(
                        toolUseName,
                        toolUseInput,
                        stepTrace.Id
                    );
                    await writer.WriteAsync(new ToolUseEvent
                    {
                        Id = toolUseId.ToString(),
                        Name = toolUseName,
                        Input = toolUseInput,
                    }, ct);
                    break;

                case "ToolResult":
                    var toolResultName = ExtractActionName(stepTrace.Title);
                    if (toolResultName.IndexOf("AskUser") != -1) break;

                    var toolResultObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(stepTrace.Content);
                    var toolResultId = await SaveToolResultAsync(
                        stepTrace.Id,
                        stepTrace.Status == "success" ? 1 : 2,
                        toolResultObj["output"]?.ToString(),
                        ExtractDuration(stepTrace.Description) * 1000
                    );
                    await writer.WriteAsync(new ToolResultEvent()
                    {
                        ToolUseId = toolResultId.ToString(),
                        Content = toolResultObj["output"]?.ToString(),
                        DurationMs = ExtractDuration(stepTrace.Description) * 1000,
                        IsError = !(stepTrace.Status == "success")
                    });
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
            foreach (var chunk in SplitText(thinking, Random.Shared.Next(10, 30)))
            {
                await writer.WriteAsync(new ContentBlockDeltaEvent
                {
                    Index = blockIndex,
                    Delta = new ContentBlockDelta { DeltaType = "thinking_delta", Thinking = chunk }
                }, ct);
                await Task.Delay(200, ct);
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
                foreach (var chunk in SplitText(result, Random.Shared.Next(10, 30)))
                {
                    await writer.WriteAsync(new ContentBlockDeltaEvent
                    {
                        Index = blockIndex,
                        Delta = new ContentBlockDelta { DeltaType = "text_delta", Text = chunk }
                    }, ct);
                    await Task.Delay(200, ct);
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
            return actionName;
        }
        private static decimal ExtractDuration(string description)
        {
            var match = System.Text.RegularExpressions.Regex.Match(description, @"耗时\s+([\d.]+)\s+秒");
            return match.Success ? decimal.Parse(match.Groups[1].Value) : 0;
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

        private static TaskState MapTaskState(string status)
        {
            return status?.ToLower() switch
            {
                "pending" => TaskState.Pending,
                "inprogress" => TaskState.InProgress,
                "completed" => TaskState.Completed,
                "failed" => TaskState.Failed,
                _ => TaskState.Pending
            };
        }

        private static int EstimateTokenCount(string text)
        {
            return (int)Math.Ceiling((text.Length / 4.0));
        }

        private async Task EmitCitations(ChannelWriter<ISseEvent> writer, List<CitationItem> citationItems, CancellationToken ct)
        {
            await writer.WriteAsync(new CitationsEvent { Citations = citationItems }, ct);
        }

        private async Task InjectTaskMemory(string taskMemory)
        {
            var sandboxContext = _agentExecutionContext.GetSandboxContext();
            var fullPath = Path.Combine(sandboxContext.SessionDir, "MEMORY.md");
            var directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(fullPath, taskMemory, Encoding.UTF8);
        }

        #region Trace Persistence

        /// <summary>
        /// 持久化推理过程
        /// </summary>
        private async Task SaveReasoningAsync(string content)
        {
            if (string.IsNullOrEmpty(content)) return;

            await _dbLock.WaitAsync();
            try
            {
                await _reasoningRepository.AddAsync(new ChatMessageReasoning
                {
                    RunId = _agentExecutionContext.GetRunId(),
                    MessageId = _agentExecutionContext.GetMessageId(),
                    Content = content
                });
            }
            finally
            {
                _dbLock.Release();
            }
        }

        /// <summary>
        /// 持久化工具调用
        /// </summary>
        private async Task<long> SaveToolCallAsync(string name, Dictionary<string, object>? input, string? output, int status, long? durationMs)
        {
            await _dbLock.WaitAsync();
            try
            {
                var toolCall = await _toolCallRepository.AddAsync(new ChatMessageToolCall
                {
                    RunId = _agentExecutionContext.GetRunId(),
                    MessageId = _agentExecutionContext.GetMessageId(),
                    Name = name,
                    Input = input,
                    Output = output,
                    Status = status,
                    DurationMs = durationMs,
                    TraceId = Guid.NewGuid().ToString("N")
                });

                return toolCall.Id;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        private async Task<long> SaveToolUseAsync(string name, Dictionary<string, object>? input, string traceId)
        {
            await _dbLock.WaitAsync();
            try
            {
                var toolCall = await _toolCallRepository.AddAsync(new ChatMessageToolCall
                {
                    RunId = _agentExecutionContext.GetRunId(),
                    MessageId = _agentExecutionContext.GetMessageId(),
                    Name = name,
                    Input = input,
                    Output = string.Empty,
                    Status = 0,
                    DurationMs = 0,
                    TraceId = traceId
                });

                return toolCall.Id;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        private async Task<long> SaveToolResultAsync(string traceId, int status, string result, decimal? durationMs)
        {
            var toolCall = await _toolCallRepository.FindAsync(x => x.TraceId == traceId);
            toolCall.Output = result;
            toolCall.Status = status;
            toolCall.DurationMs = durationMs;

            await _toolCallRepository.UpdateAsync(toolCall);
            return toolCall.Id;
        }


        /// <summary>
        /// 保存或更新计划/子任务
        /// </summary>
        private async Task SavePlanAsync(int planId, string title, string description, string? output, int status)
        {
            await _dbLock.WaitAsync();
            try
            {
                var runId = _agentExecutionContext.GetRunId();
                var messageId = _agentExecutionContext.GetMessageId();

                var existing = await _planRepository.FindAsync(x => x.RunId == runId && x.MessageId == messageId && x.PlanId == planId);
                if (existing != null)
                {
                    existing.Title = title;
                    existing.Description = description;
                    existing.Output = output ?? "";
                    existing.Status = status;
                    await _planRepository.UpdateAsync(existing);
                }
                else
                {
                    await _planRepository.AddAsync(new ChatMessagePlan
                    {
                        RunId = runId,
                        MessageId = messageId,
                        PlanId = planId,
                        Title = title,
                        Description = description,
                        Output = output ?? "",
                        Status = status
                    });
                }
            }
            finally
            {
                _dbLock.Release();
            }
        }
        #endregion
    }
}
