using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Context.Compaction;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Mcp;
using InsightaAI.Agent.Memory;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Prompts;
using InsightaAI.Agent.Skills;
using InsightaAI.Agent.Storage;
using InsightaAI.Agent.Tools;
using InsightaAI.Agent.Tools.BuiltIn;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace InsightaAI.Agent;

/// <summary>
/// Agent 运行时 - 实现 Agent Loop 模式
/// </summary>
public class Agent : IDisposable
{
    /// <summary>
    /// Hook 执行错误事件（用于日志记录）
    /// </summary>
    public static event Action<string, Exception>? OnHookError;
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
    private readonly HashSet<string> _alwaysAllowedTools = [];
    private string _skillInstructions = "";
    private readonly IServiceProvider _serviceProvider;
    private bool _disposed;

    /// <summary>
    /// 可选的 ToolCallHandler 装饰器 — 用于注入 telemetry 等横切关注点。
    /// 设置后，RunStreamAsync 中创建的 handler 会先经过此装饰器包装。
    /// </summary>
    public Func<ToolCallHandler, ToolCallHandler>? ToolCallHandlerDecorator { get; set; }

    /// <summary>
    /// 可选的 ILlmClient 装饰器 — 用于注入 telemetry 等横切关注点。
    /// 设置后，RunStreamAsync 中传给 AgentLoop 的 LLM 客户端会经过此装饰器包装。
    /// </summary>
    public Func<ILlmClient, ILlmClient>? LlmClientDecorator { get; set; }

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
        if (_skillRegistry != null) services.AddSingleton(_skillRegistry);
        if (_mcpRegistry != null) services.AddSingleton(_mcpRegistry);
        if (_contextManager != null) services.AddSingleton(_contextManager);
        if (_memoryManager != null) services.AddSingleton(_memoryManager);
        _serviceProvider = services.BuildServiceProvider();

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
            "激活一个技能以获得相关指导。当用户任务需要特定技能时使用。",
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

                // 追加 Instructions
                _skillInstructions += "\n\n" + skill.Instructions;

                return ToolResult.FromText($"Skill '{skillName}' activated successfully. Instructions have been loaded.");
            });
    }

    /// <summary>
    /// 获取可用 Skills 信息（用于构建 SystemPrompt）
    /// </summary>
    private async Task<string> GetAvailableSkillsAsync(CancellationToken cancellationToken = default)
    {
        if (_skillRegistry == null)
        {
            return "";
        }

        var skills = await _skillRegistry.ListAllSkillsAsync(cancellationToken);
        if (skills.Count == 0)
        {
            return "";
        }

        var skillsList = string.Join("\n", skills.Select(s => $"- **{s.Name}**: {s.Description}"));
        return await PromptTemplate.RenderAsync("available-skills", new Dictionary<string, string>
        {
            ["skills_list"] = skillsList
        });
    }

    /// <summary>
    /// 获取可用 MCP 服务器信息（用于构建 SystemPrompt）
    /// </summary>
    private async Task<string> GetAvailableMcpServersAsync(CancellationToken cancellationToken = default)
    {
        if (_mcpRegistry == null)
        {
            return "";
        }

        var servers = await _mcpRegistry.ListAllServersAsync(cancellationToken);
        if (servers.Count == 0)
        {
            return "";
        }

        var serversList = string.Join("\n", servers.Select(s => $"- **{s.Name}**: {s.Description}"));
        return await PromptTemplate.RenderAsync("available-mcps", new Dictionary<string, string>
        {
            ["mcp_servers_list"] = serversList
        });
    }

    /// <summary>
    /// 获取记忆上下文（注入到 SystemPrompt）
    /// </summary>
    private async Task<string> GetMemoryContextAsync(CancellationToken cancellationToken = default)
    {
        if (_memoryManager == null || string.IsNullOrEmpty(_config.UserId))
        {
            return "";
        }

        try
        {
            // 获取 MEMORY.md 索引（按需加载，不加载全部内容）
            var memoryIndex = await _memoryManager.GetMemoryIndexAsync(
                _config.UserId, null, cancellationToken);

            return await PromptTemplate.RenderAsync("available-memories", new Dictionary<string, string>
            {
                ["memory_index"] = string.IsNullOrWhiteSpace(memoryIndex) ? "_No memories stored yet._" : memoryIndex
            });
        }
        catch
        {
            // 记忆系统出错不应阻止对话
            return "";
        }
    }

    /// <summary>
    /// 触发 agent 级别的启动钩子（在任何轮次开始前调用）
    /// </summary>
    private async Task TriggerSessionStartedHooksAsync(
        AgentEventHookContext context,
        string message,
        CancellationToken cancellationToken)
    {
        if (_agentHooks.Count == 0)
            return;

        foreach (var hook in _agentHooks)
        {
            try
            {
                await hook.OnAgentSessionStartedAsync(context, message, cancellationToken);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AgentHook] Agent started hook '{hook.Id}' failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 触发 agent 级别的轮次开始钩子（同步等待，确保 Activity.Current 在 LLM 调用前就位）
    /// </summary>
    private async Task TriggerRoundStartedHooksAsync(
        string message,
        CancellationToken cancellationToken)
    {
        if (_agentHooks.Count == 0)
            return;

        foreach (var hook in _agentHooks)
        {
            try
            {
                await hook.OnAgentRoundStartedAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AgentHook] Round start hook '{hook.Id}' failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 触发 agent 级别的轮次结束钩子（fire-and-forget）
    /// 注意：此方法返回 void，明确表示不等待完成
    /// </summary>
    private void TriggerRoundEndedHooks(
        AgentEventHookContext hookContext,
        int round,
        List<Message> messages,
        Message? assistantMessage,
        CancellationToken cancellationToken)
    {
        if (_agentHooks.Count == 0)
            return;

        // Fire-and-forget: 并行触发所有 hooks，不阻塞主流程
        var tasks = _agentHooks.Select(hook =>
            hook.OnAgentRoundEndedAsync(hookContext, round, messages, assistantMessage, cancellationToken));

        _ = Task.WhenAll(tasks).ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception != null)
            {
                var innerEx = t.Exception.InnerException ?? t.Exception;
                System.Diagnostics.Debug.WriteLine(
                    $"[AgentHook] Round {round} hooks failed: {innerEx.Message}");

                // 触发错误事件，允许外部日志系统记录
                try
                {
                    OnHookError?.Invoke($"Round {round} hooks failed", innerEx);
                }
                catch { /* 错误事件处理器自身不能抛出异常 */ }
            }
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// 触发 agent 级别的会话结束钩子
    /// </summary>
    private async Task TriggerSessionEndedHooksAsync(
        AgentEventHookContext context,
        List<Message> messages,
        CancellationToken cancellationToken)
    {
        if (_agentHooks.Count == 0)
            return;

        foreach (var hook in _agentHooks)
        {
            try
            {
                await hook.OnAgentSessionEndedAsync(context, messages, cancellationToken);
            }
            catch (Exception e)
            {
                // 忽略 hook 执行错误
            }
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
        var hookContext = new AgentEventHookContext
        {
            SessionId = sessionId,
            Services = _serviceProvider
        };

        // 每次调用创建 AgentLoop，确保 sessionId 正确
        ToolCallHandler handler = async (request, ct) =>
        {
            var (allowed, result) = await ExecuteSingleToolAsync(request.ToolCall, request.Arguments, request.SessionId, ct);
            return new ToolCallReponse(allowed, result);
        };
        if (ToolCallHandlerDecorator != null)
            handler = ToolCallHandlerDecorator(handler);
        var toolCallExecutor = new ToolCallExecutor(_config.Id, sessionId, handler, _serviceProvider);
        var llmClient = LlmClientDecorator != null ? LlmClientDecorator(_llmClient) : _llmClient;
        var agentLoop = new AgentLoop(_config, llmClient, _toolRegistry, toolCallExecutor, () => _skillInstructions);

        // 构建 LoopContext（System Prompt + History + User Input）
        var loopContext = new LoopContext(sessionId, _config.Id, _contextManager);

        // 构建系统提示词（包含可用 Skills 信息和记忆索引）
        var systemPrompt = _config.SystemPrompt ?? "";
        systemPrompt += await GetAvailableSkillsAsync(cancellationToken);
        systemPrompt += await GetAvailableMcpServersAsync(cancellationToken);

        // 注入记忆索引（按需加载 MEMORY.md）
        if (_memoryManager != null && !string.IsNullOrEmpty(_config.UserId))
        {
            var memoryContext = await GetMemoryContextAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(memoryContext))
            {
                systemPrompt += memoryContext;
            }
        }

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

        loopContext.AddMessage(Message.FromUser(input));

        await foreach (var evt in agentLoop.RunAsync(loopContext, cancellationToken))
        {
            // 转发事件给调用方
            yield return evt;

            // 事件后处理
            switch (evt)
            {
                case AgentSessionStartEvent sessionStartEvt:
                    hookContext.AttachEvent(sessionStartEvt);
                    await TriggerSessionStartedHooksAsync(hookContext, input, cancellationToken);
                    break;

                case AgentRoundStartEvent roundStartEvt:
                    hookContext.AttachEvent(roundStartEvt);
                    await TriggerRoundStartedHooksAsync(input, cancellationToken);
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

                case AgentRoundEndEvent roundEndEvt:
                    hookContext.AttachEvent(roundEndEvt);
                    var lastAssistantMessage = loopContext.Messages
                        .LastOrDefault(m => m.Role == MessageRole.Assistant);
                    TriggerRoundEndedHooks(hookContext, roundEndEvt.Round,
                        loopContext.Messages.ToList(), lastAssistantMessage, cancellationToken);
                    break;

                case AgentSessionEndEvent sessionEndEvent:
                    hookContext.AttachEvent(sessionEndEvent);
                    await TriggerSessionEndedHooksAsync(hookContext,
                        loopContext.Messages.ToList(), cancellationToken);
                    break;
            }
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

        if (!string.IsNullOrEmpty(_config.SystemPrompt))
        {
            messages.Add(Message.FromSystem(_config.SystemPrompt));
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
            if (evt is AgentSessionEndEvent completeEvent)
            {
                result = completeEvent.Result;
            }
        }

        return result ?? new AgentResult
        {
            Status = AgentStatus.Failed,
            Message = Message.FromAssistant("Agent execution failed."),
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
