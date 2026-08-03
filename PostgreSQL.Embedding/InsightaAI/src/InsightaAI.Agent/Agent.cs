using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Context.Compaction;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Mcp;
using InsightaAI.Agent.Memory;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Skills;
using InsightaAI.Agent.Storage;
using InsightaAI.Agent.Tools;
using InsightaAI.Agent.Tools.BuiltIn;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace InsightaAI.Agent;

/// <summary>
/// Agent 运行时 - 实现 Agent Loop 模式
/// </summary>
public class Agent : IDisposable
{
    private readonly AgentConfig _config;
    private readonly ILlmClient _llmClient;
    private readonly ToolRegistry _toolRegistry;
    private readonly ISkillRegistry? _skillRegistry;
    private readonly McpRegistry? _mcpRegistry;
    private readonly IContextManager? _contextManager;
    private readonly IMemoryManager? _memoryManager;
    private readonly IMessageStorage? _messageStorage;
    private readonly List<IToolHook> _toolHooks = [];
    private readonly List<IAgentEventHook> _agentHooks = [];
    private readonly List<IUserPromptEventHook> _userPromptHooks = [];
    private readonly HashSet<string> _alwaysAllowedTools = [];
    private readonly List<ISkill> _activatedSkills = [];
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Agent> _logger;
    private string? _agentsMd;
    private bool _agentsMdLoaded;
    private bool _disposed;

    /// <summary>
    /// 可选的 ToolCallHandler 装饰器 — 用于注入 telemetry 等横切关注点。
    /// 设置后，RunStreamAsync 中创建的 handler 会先经过此装饰器包装。
    /// </summary>
    public Func<ToolCallHandler, ToolCallHandler>? ToolCallHandlerProxyFactory { get; set; }

    /// <summary>
    /// 可选的 ILlmClient 装饰器 — 用于注入 telemetry 等横切关注点。
    /// 设置后，RunStreamAsync 中传给 AgentLoop 的 LLM 客户端会经过此装饰器包装。
    /// </summary>
    public Func<ILlmClient, ILlmClient>? LlmClientProxyFactory { get; set; }

    /// <summary>
    /// 创建 Agent 实例（手动注入依赖）
    /// </summary>
    public Agent(
        AgentConfig config,
        ILlmClient llmClient,
        ToolRegistry toolRegistry,
        ISkillRegistry? skillRegistry = null,
        McpRegistry? mcpRegistry = null,
        IContextManager? contextManager = null,
        IMemoryManager? memoryManager = null,
        IMessageStorage? messageStorage = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(llmClient);
        ArgumentNullException.ThrowIfNull(toolRegistry);

        _config = config;
        _llmClient = llmClient;
        _toolRegistry = toolRegistry;
        _skillRegistry = skillRegistry;
        _mcpRegistry = mcpRegistry;
        _contextManager = contextManager;
        _memoryManager = memoryManager;
        _messageStorage = messageStorage;

        // 构建内部 ServiceProvider，让 Hook / Tool 可按需解析服务
        var services = new ServiceCollection();
        services.AddSingleton<ILlmClient>(_llmClient);
        services.AddSingleton(_toolRegistry);
        services.AddSingleton<IEnvironmentVariableReader, ProcessEnvironmentVariableReader>();
        if (_skillRegistry != null) services.AddSingleton(_skillRegistry);
        if (_mcpRegistry != null) services.AddSingleton(_mcpRegistry);
        if (_contextManager != null) services.AddSingleton(_contextManager);
        if (_memoryManager != null) services.AddSingleton(_memoryManager);
        _serviceProvider = services.BuildServiceProvider();
        _logger = NullLogger<Agent>.Instance;

        // 注册 activate_skill 工具
        if (_skillRegistry != null)
        {
            RegisterActivateSkillTool();
        }

        // 注册 MCP 工具
        if (_mcpRegistry != null)
        {
            McpTools.RegisterAll(_toolRegistry, _mcpRegistry);
        }

        // 注册记忆工具
        if (_memoryManager != null && !string.IsNullOrEmpty(_config.UserId))
        {
            MemoryTools.RegisterAll(_toolRegistry, _memoryManager, _config.UserId);
        }
    }

    /// <summary>
    /// 创建 Agent 实例（通过 ServiceProvider 解析依赖）
    /// </summary>
    public Agent(AgentConfig config, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _config = config;
        _serviceProvider = serviceProvider;
        _llmClient = serviceProvider.GetRequiredService<ILlmClient>();
        _toolRegistry = serviceProvider.GetRequiredService<ToolRegistry>();
        _skillRegistry = serviceProvider.GetService<ISkillRegistry>();
        _mcpRegistry = serviceProvider.GetService<McpRegistry>();
        _contextManager = serviceProvider.GetService<IContextManager>();
        _memoryManager = serviceProvider.GetService<IMemoryManager>();
        _messageStorage = serviceProvider.GetService<IMessageStorage>();
        _logger = serviceProvider.GetService<ILogger<Agent>>() ?? NullLogger<Agent>.Instance;

        // 注册 activate_skill 工具
        if (_skillRegistry != null)
        {
            RegisterActivateSkillTool();
        }

        // 注册 MCP 工具
        if (_mcpRegistry != null)
        {
            McpTools.RegisterAll(_toolRegistry, _mcpRegistry);
        }

        // 注册记忆工具
        if (_memoryManager != null && !string.IsNullOrEmpty(_config.UserId))
        {
            MemoryTools.RegisterAll(_toolRegistry, _memoryManager, _config.UserId);
        }
    }

    /// <summary>
    /// Agent 配置
    /// </summary>
    public AgentConfig Config => _config;

    /// <summary>
    /// 添加工具调用钩子
    /// </summary>
    public Agent AddHook(IToolHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _toolHooks.Add(hook);
        return this;
    }

    /// <summary>
    /// 添加 Agent 级别钩子（轮次/会话级别）
    /// </summary>
    public Agent AddAgentHook(IAgentEventHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _agentHooks.Add(hook);
        return this;
    }

    /// <summary>
    /// 添加用户输入后置 Hook。Hook 以 fire-and-forget 方式执行，不能拦截当前输入。
    /// </summary>
    public Agent AddUserPromptHook(IUserPromptEventHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _userPromptHooks.Add(hook);
        return this;
    }

    /// <summary>
    /// 注册 activate_skill 工具
    /// </summary>
    private void RegisterActivateSkillTool()
    {
        var schema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""skill_name"": {
                    ""type"": ""string"",
                    ""description"": ""要激活的技能名称""
                }
            },
            ""required"": [""skill_name""]
        }");

        _toolRegistry.RegisterFunction(
            "activate_skill",
            "Activate a skill to get specialized guidance for the current task. If a skill matches the task, activate it before proceeding.",
            schema,
            async (args, ctx) =>
            {
                var skillName = args["skill_name"]?.ToString();
                if (string.IsNullOrEmpty(skillName))
                {
                    return ToolResult.FromError("skill_name is required");
                }

                var skill = await _skillRegistry!.ActivateAsync(skillName, ctx.CancellationToken);
                if (skill == null)
                {
                    return ToolResult.FromError($"Skill '{skillName}' not found");
                }

                if (!_activatedSkills.Any(s => s.Metadata.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase)))
                {
                    _activatedSkills.Add(skill);
                }

                return ToolResult.FromText($"Skill '{skillName}' activated successfully. The full skill instructions will be available in the next reasoning round. Follow them before answering the user.");
            });

        RegisterListSkillsTool();
    }

    /// <summary>
    /// 注册 list_skills 工具
    /// </summary>
    private void RegisterListSkillsTool()
    {
        var schema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {},
            ""required"": []
        }");

        _toolRegistry.RegisterFunction(
            "list_skills",
            "List all available skills (name and description). Use to check if any skill matches the current task.",
            schema,
            async (args, ctx) =>
            {
                var skills = await _skillRegistry!.ListAllSkillsAsync(ctx.CancellationToken);
                if (skills.Count == 0)
                {
                    return ToolResult.FromText("No skills available.");
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Available skills ({skills.Count}):");
                sb.AppendLine();
                foreach (var skill in skills)
                {
                    var active = _skillRegistry.IsActive(skill.Name) ? " [active]" : "";
                    sb.AppendLine($"- **{skill.Name}**{active}: {skill.Description}");
                }

                return ToolResult.FromText(sb.ToString());
            });
    }

    /// <summary>
    /// 构建完整的 System Prompt（每轮发送前调用，保证反映最新的 Skills/MCP/Memory 状态）
    /// </summary>
    private async Task<string> BuildSystemPromptAsync(
        ActiveMemorySnapshot? memorySnapshot = null,
        CancellationToken cancellationToken = default)
    {
        var allSkills = _skillRegistry != null
            ? await _skillRegistry.ListAllSkillsAsync(cancellationToken)
            : null;

        var mcps = _mcpRegistry != null
            ? await _mcpRegistry.ListAllServersAsync(cancellationToken)
            : null;

        var memoryIndex = memorySnapshot is null ? null : FormatMemorySnapshot(memorySnapshot);

        return await Context.SystemPrompt.SystemPromptBuilder.BuildAsync(new Context.SystemPrompt.SystemPromptParams
        {
            CustomInstructions = _config.CustomInstructions,
            AgentsMd = LoadAgentsMd(),
            AllSkills = allSkills,
            ActivatedSkills = _activatedSkills,
            McpServers = mcps,
            MemoryIndex = memoryIndex,
        });
    }

    /// <summary>Formats the frozen memory snapshot for the dynamic system prompt.</summary>
    private static string? FormatMemorySnapshot(ActiveMemorySnapshot snapshot)
    {
        if (snapshot.Entries.Count == 0)
            return snapshot.Index;

        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(snapshot.Index))
            sb.AppendLine(snapshot.Index);

        AppendMemories("Core memories:", snapshot.CoreEntries);
        AppendMemories("Task-related memories for this turn:", snapshot.ActiveEntries);
        return sb.ToString().TrimEnd();

        void AppendMemories(string title, IReadOnlyList<MemoryEntry> memories)
        {
            if (memories.Count == 0)
                return;

            sb.AppendLine(title);
            foreach (var memory in memories)
            {
                sb.Append($"- [{memory.Type}] {memory.Name}: {memory.Description}");
                if (memory.Tags.Count > 0)
                    sb.Append($" (tags: {string.Join(", ", memory.Tags)})");
                sb.AppendLine();
            }
        }
    }

    /// <summary>
    /// 加载工作目录中的 AGENTS.md（懒加载，仅加载一次）
    /// </summary>
    private string? LoadAgentsMd()
    {
        if (_agentsMdLoaded) return _agentsMd;
        _agentsMdLoaded = true;

        var workDir = _config.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workDir)) return null;

        var path = Path.Combine(workDir, "AGENTS.md");
        if (!File.Exists(path)) return null;

        _agentsMd = File.ReadAllText(path);
        return _agentsMd;
    }

    /// <summary>
    /// 触发 Agent Turn 启动钩子（fire-and-forget，不阻塞 Agent 主循环）
    /// </summary>
    private void TriggerTurnStartedHooks(
        AgentEventHookContext context,
        string message,
        CancellationToken cancellationToken)
    {
        if (_agentHooks.Count == 0)
            return;

        foreach (var hook in _agentHooks)
        {
            _ = SafeInvokeHookAsync(() => hook.OnAgentTurnStartedAsync(
                context, message, cancellationToken),
                $"Turn start hook '{hook.Id}'");
        }
    }

    private void TriggerUserPromptHooks(
        AgentEventHookContext context,
        Message userMessage,
        CancellationToken cancellationToken)
    {
        if (_userPromptHooks.Count == 0)
            return;

        foreach (var hook in _userPromptHooks)
        {
            _ = SafeInvokeHookAsync(() => hook.OnUserPromptReceivedAsync(
                context, userMessage, cancellationToken),
                $"User prompt hook '{hook.Id}'");
        }
    }

    /// <summary>
    /// 触发 agent 级别的轮次开始钩子（fire-and-forget，不阻塞 Agent 主循环）
    /// </summary>
    private void TriggerRoundStartedHooks(
        AgentEventHookContext context,
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken)
    {
        if (_agentHooks.Count == 0)
            return;

        foreach (var hook in _agentHooks)
        {
            _ = SafeInvokeHookAsync(() => hook.OnAgentRoundStartedAsync(
                context, messages, cancellationToken),
                $"Round start hook '{hook.Id}'");
        }
    }

    /// <summary>
    /// 触发 agent 级别的轮次结束钩子（fire-and-forget，不阻塞 Agent 主循环）
    /// </summary>
    private void TriggerRoundEndedHooks(
        AgentEventHookContext hookContext,
        IReadOnlyList<Message> messages,
        Message? assistantMessage,
        CancellationToken cancellationToken)
    {
        if (_agentHooks.Count == 0)
            return;

        foreach (var hook in _agentHooks)
        {
            _ = SafeInvokeHookAsync(() => hook.OnAgentRoundEndedAsync(
                hookContext, messages, assistantMessage, cancellationToken),
                $"Round {hookContext.GetEvent<AgentRoundEndEvent>().Round} end hook '{hook.Id}'");
        }
    }

    private AgentEventHookContext CreateHookContext(string sessionId, AgentEvent @event)
    {
        return AgentEventHookContext.Create(sessionId, @event, _serviceProvider);
    }

    private void TriggerErrorHooks(AgentEventHookContext context, CancellationToken cancellationToken)
    {
        foreach (var hook in _agentHooks)
        {
            _ = SafeInvokeHookAsync(() => hook.OnAgentErrorAsync(context, cancellationToken),
                $"Error hook '{hook.Id}'");
        }
    }

    /// <summary>
    /// 触发 Agent Turn 结束钩子（fire-and-forget，不阻塞 Agent 主循环）
    /// </summary>
    private void TriggerTurnEndedHooks(
        AgentEventHookContext context,
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken)
    {
        if (_agentHooks.Count == 0)
            return;

        foreach (var hook in _agentHooks)
        {
            _ = SafeInvokeHookAsync(() => hook.OnAgentTurnEndedAsync(
                context, messages, cancellationToken),
                $"Turn end hook '{hook.Id}'");
        }
    }

    /// <summary>
    /// 安全地 fire-and-forget 调用 Hook，异常仅记录日志
    /// </summary>
    private Task SafeInvokeHookAsync(Func<Task> hookAction, string hookLabel)
    {
        return hookAction().ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception != null)
            {
                var ex = t.Exception.InnerException ?? t.Exception;
                _logger.LogWarning("[AgentHook] {HookLabel} failed: {Message}", hookLabel, ex.Message);
            }
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// 记录 Agent 事件日志（跳过 LLM 流式事件，避免日志爆炸）
    /// </summary>
    private void LogEvent(AgentEvent evt, string sessionId)
    {
        switch (evt)
        {
            case AgentUserPromptEvent userPrompt:
                _logger.LogInformation(
                    "[{SessionId}] User prompt received — input={Input}",
                    sessionId, userPrompt.Input);
                break;

            case AgentTurnStartEvent turnStart:
                _logger.LogInformation(
                    "[{SessionId}] Turn started — agent={AgentId}, model={Model}",
                    sessionId, turnStart.AgentId, turnStart.Model);
                break;

            case AgentRoundStartEvent roundStart:
                _logger.LogInformation(
                    "[{SessionId}] Round {Round} started",
                    sessionId, roundStart.Round);
                break;

            case AgentToolStartEvent toolStart:
                var args = toolStart.Arguments?.Length > 100
                    ? toolStart.Arguments[..100] + "..."
                    : toolStart.Arguments;
                _logger.LogInformation(
                    "[{SessionId}] Tool {ToolName}({Arguments}) started — callId={CallId}",
                    sessionId, toolStart.ToolName, args, toolStart.ToolCallId);
                break;

            case AgentToolEndEvent toolEnd:
                _logger.LogInformation(
                    "[{SessionId}] Tool {ToolName} completed — isError={IsError}, callId={CallId}",
                    sessionId, toolEnd.ToolName, toolEnd.IsError, toolEnd.ToolCallId);
                break;

            case AgentRoundEndEvent roundEnd:
                _logger.LogInformation(
                    "[{SessionId}] Round {Round} ended — hasToolCalls={HasToolCalls}",
                    sessionId, roundEnd.Round, roundEnd.HasToolCalls);
                break;

            case AgentTurnEndEvent turnEnd:
                _logger.LogInformation(
                    "[{SessionId}] Turn ended — status={Status}, rounds={Rounds}, duration={Duration}ms, " +
                    "inputTokens={InputTokens}, outputTokens={OutputTokens}, contextTokens={ContextTokens}/{MaxTokens}",
                    sessionId, turnEnd.Result.Status, turnEnd.Result.Rounds, turnEnd.Result.DurationMs,
                    turnEnd.Result.Usage?.InputTokens ?? 0, turnEnd.Result.Usage?.OutputTokens ?? 0,
                    turnEnd.Result.EstimatedContextTokens, turnEnd.Result.MaxContextTokens);
                break;

            case AgentErrorEvent error:
                _logger.LogError(
                    "[{SessionId}] Agent error — message={Message}, recoverable={Recoverable}",
                    sessionId, error.ErrorMessage, error.Recoverable);
                break;

            case AgentContextCompactedEvent compacted:
                _logger.LogInformation(
                    "[{SessionId}] Context compacted — strategy={Strategy}, " +
                    "tokens: {PreTokens}→{PostTokens}, messages: {PreMessages}→{PostMessages}",
                    sessionId, compacted.Strategy,
                    compacted.PreCompactTokens, compacted.PostCompactTokens,
                    compacted.PreCompactMessages, compacted.PostCompactMessages);
                break;
        }
    }

    /// <summary>
    /// 执行钩子检查
    /// </summary>
    private async Task<bool> CheckToolPermissionAsync(
        string toolName,
        string arguments,
        ToolExecutionContext context)
    {
        // 如果工具已被标记为始终允许，跳过检查
        if (_alwaysAllowedTools.Contains(toolName))
        {
            return true;
        }

        foreach (var hook in _toolHooks)
        {
            // 检查钩子是否适用于该工具
            if (hook.TargetTools != null && !hook.TargetTools.Contains(toolName))
            {
                continue; // 跳过不适用的钩子
            }

            var result = await hook.OnBeforeExecutionAsync(toolName, arguments, context);

            switch (result)
            {
                case ToolHookResult.Allow:
                    continue; // 检查下一个钩子

                case ToolHookResult.AllowAlways:
                    _alwaysAllowedTools.Add(toolName);
                    return true; // 标记为始终允许，继续执行

                case ToolHookResult.Deny:
                    return false; // 拒绝执行

                default:
                    continue;
            }
        }

        return true; // 所有钩子都允许
    }

    /// <summary>
    /// 执行 Agent (流式)
    /// </summary>
    public async IAsyncEnumerable<AgentEvent> RunStreamAsync(
        string input,
        AgentContext? context = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = context?.SessionId ?? Guid.NewGuid().ToString("N");
        ActiveMemorySnapshot? memorySnapshot = null;
        if (_memoryManager != null && !string.IsNullOrEmpty(_config.UserId))
        {
            try
            {
                memorySnapshot = await _memoryManager.CreateActiveMemorySnapshotAsync(
                    _config.UserId, input, sessionId, cancellationToken: cancellationToken);
            }
            catch
            {
                // Memory retrieval must not prevent a conversation from starting.
            }
        }
        // 每次调用创建 AgentLoop，确保 sessionId 正确
        ToolCallHandler handler = async (request, ct) =>
        {
            var (allowed, result) = await ExecuteSingleToolAsync(request.ToolCall, request.Arguments, request.SessionId, ct);
            return new ToolCallReponse(allowed, result);
        };
        if (ToolCallHandlerProxyFactory != null)
            handler = ToolCallHandlerProxyFactory(handler);
        var toolCallExecutor = new ToolCallExecutor(_config.Id, sessionId, handler, _serviceProvider);
        var llmClient = LlmClientProxyFactory != null ? LlmClientProxyFactory(_llmClient) : _llmClient;
        var agentLoop = new AgentLoop(_config, llmClient, _toolRegistry, toolCallExecutor,
            cancellationToken => BuildSystemPromptAsync(memorySnapshot, cancellationToken));

        // 构建 LoopContext（System Prompt + History + User Input）
        var loopContext = new LoopContext(sessionId, _config.Id, _contextManager);

        // 构建系统提示词（每轮 AgentLoop 内部会重建以反映 Skills 激活等动态状态）
        var systemPrompt = await BuildSystemPromptAsync(memorySnapshot, cancellationToken);
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            loopContext.AddMessage(Message.FromSystem(systemPrompt));
        }

        if (context?.History != null)
        {
            loopContext.AddMessages(context.History);
        }

        // 设置消息持久化回调（在 history 之后、user message 之前）
        // 这样只有新增的消息会被持久化，历史消息不会重复存储
        if (_messageStorage != null)
        {
            loopContext.OnMessageAdded = msg =>
            {
                // 系统消息不持久化（运行时构建的 prompt）
                if (msg.Role == MessageRole.System) return;
                var record = msg.ToMessageRecord(sessionId);
                _ = _messageStorage.AddMessageAsync(sessionId, record);
            };
        }

        var userMessage = Message.FromUser(input);
        loopContext.AddMessage(userMessage);

        var userPromptEvent = new AgentUserPromptEvent() { AgentId = sessionId, Input = input };
        LogEvent(userPromptEvent, sessionId);

        TriggerUserPromptHooks(
            CreateHookContext(sessionId, userPromptEvent),
            userMessage,
            cancellationToken);

        await foreach (var evt in agentLoop.RunAsync(loopContext, cancellationToken))
        {
            // 事件日志
            LogEvent(evt, sessionId);

            // 事件预处理：Hook 与副作用在 yield 前完成，确保消费者提前退出时不会丢失关键工作
            switch (evt)
            {
                case AgentTurnStartEvent turnStartEvt:
                    TriggerTurnStartedHooks(
                        CreateHookContext(sessionId, turnStartEvt), input, cancellationToken);
                    break;

                case AgentRoundStartEvent roundStartEvt:
                    TriggerRoundStartedHooks(
                        CreateHookContext(sessionId, roundStartEvt),
                        loopContext.Messages.ToArray(),
                        cancellationToken);
                    break;

                case AgentContextCompactedEvent compactedEvt:
                    if (_messageStorage != null && compactedEvt.CompactedMessages is { Length: > 0 } compacted)
                    {
                        await _messageStorage.ClearMessagesAsync(sessionId);
                        foreach (var msg in compacted)
                        {
                            if (msg.Role == MessageRole.System) continue;
                            var record = msg.ToMessageRecord(sessionId);
                            await _messageStorage.AddMessageAsync(sessionId, record);
                        }
                    }
                    break;

                case AgentErrorEvent errorEvent:
                    TriggerErrorHooks(CreateHookContext(sessionId, errorEvent), cancellationToken);
                    break;

                case AgentRoundEndEvent roundEndEvt:
                    var lastAssistantMessage = loopContext.Messages
                        .LastOrDefault(m => m.Role == MessageRole.Assistant);
                    TriggerRoundEndedHooks(CreateHookContext(sessionId, roundEndEvt),
                        loopContext.Messages.ToArray(), lastAssistantMessage, cancellationToken);
                    break;

                case AgentTurnEndEvent turnEndEvent:
                    TriggerTurnEndedHooks(CreateHookContext(sessionId, turnEndEvent),
                        loopContext.Messages.ToArray(), cancellationToken);
                    break;
            }

            // 转发事件给调用方
            yield return evt;
        }
    }

    /// <summary>
    /// 强制执行上下文压缩
    /// </summary>
    /// <param name="strategy">压缩策略: auto, micro, traditional</param>
    /// <param name="context">Agent 上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩结果，如果无需压缩则返回 null</returns>
    public async Task<CompactionResult?> CompactContextAsync(
        string strategy = "auto",
        AgentContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (_contextManager == null)
            return null;

        // 构建消息列表
        var messages = new List<Message>();

        if (!string.IsNullOrEmpty(_config.CustomInstructions))
        {
            messages.Add(Message.FromSystem(_config.CustomInstructions));
        }

        if (context?.History != null)
        {
            messages.AddRange(context.History);
        }

        return await _contextManager.ForceCompactAsync(messages, strategy, cancellationToken);
    }

    /// <summary>
    /// 执行单个工具（含钩子检查）
    /// </summary>
    private async Task<(bool Allowed, ToolResult Result)> ExecuteSingleToolAsync(
        ToolCallBlock toolCall,
        string arguments,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var toolContext = new ToolExecutionContext
        {
            AgentId = _config.Id,
            ToolCallId = toolCall.Id,
            SessionId = sessionId,
            CancellationToken = cancellationToken,
            Services = _serviceProvider
        };

        // 检查钩子
        var allowed = await CheckToolPermissionAsync(toolCall.Name, arguments, toolContext);

        ToolResult toolResult;
        if (allowed)
        {
            toolResult = await _toolRegistry.ExecuteAsync(toolCall, toolContext);
        }
        else
        {
            toolResult = ToolResult.FromError("Tool execution denied by user. Use `ask_user` if need to understand reason.");
        }

        return (allowed, toolResult);
    }

    /// <summary>
    /// 执行 Agent (非流式)
    /// </summary>
    public async Task<AgentResult> RunAsync(
        string input,
        AgentContext? context = null,
        CancellationToken cancellationToken = default)
    {
        AgentResult? result = null;

        await foreach (var evt in RunStreamAsync(input, context, cancellationToken))
        {
            if (evt is AgentTurnEndEvent completeEvent)
            {
                result = completeEvent.Result;
            }
        }

        return result ?? new AgentResult
        {
            Status = AgentStatus.Failed,
            Error = "No completion event received."
        };
    }

    /// <summary>
    /// 释放资源（释放内部 ServiceProvider）
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            // 释放内部 ServiceProvider（如果是我们创建的）
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }
}
