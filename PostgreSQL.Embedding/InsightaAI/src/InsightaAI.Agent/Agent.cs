using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Context.Compaction;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Mcp;
using InsightaAI.Agent.Memory;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Prompts;
using InsightaAI.Agent.Skills;
using InsightaAI.Agent.Tools;
using InsightaAI.Agent.Tools.BuiltIn;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
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
    private readonly List<IToolHook> _toolHooks = [];
    private readonly List<IAgentHook> _agentHooks = [];
    private readonly HashSet<string> _alwaysAllowedTools = [];
    private string _skillInstructions = "";
    private readonly IServiceProvider _serviceProvider;
    private bool _disposed;

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
        IMemoryManager? memoryManager = null)
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
    public Agent AddAgentHook(IAgentHook hook)
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
    /// 触发 agent 级别的轮次结束钩子（fire-and-forget）
    /// 注意：此方法返回 void，明确表示不等待完成
    /// </summary>
    private void TriggerAgentRoundEndHooks(
        HookContext hookContext,
        int round,
        List<Message> messages,
        Message? assistantMessage,
        CancellationToken cancellationToken)
    {
        if (_agentHooks.Count == 0)
            return;

        // Fire-and-forget: 并行触发所有 hooks，不阻塞主流程
        var tasks = _agentHooks.Select(hook =>
            hook.OnRoundEndAsync(hookContext, round, messages, assistantMessage, cancellationToken));

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
    private async Task TriggerAgentSessionEndHooksAsync(
        HookContext context,
        List<Message> messages,
        CancellationToken cancellationToken)
    {
        if (_agentHooks.Count == 0)
            return;

        foreach (var hook in _agentHooks)
        {
            try
            {
                await hook.OnSessionEndAsync(context, messages, cancellationToken);
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
        var conversationId = context?.SessionId ?? Guid.NewGuid().ToString("N");
        var hookContext = new HookContext
        {
            SessionId = conversationId,
            Services = _serviceProvider
        };

        var toolExecutor = new ToolExecutor(_config.Id, conversationId, async (request, ct)  =>
        {
            var (allowd, result) = await ExecuteSingleToolAsync(request.ToolCall, request.Arguments, request.SessionId, ct);
            return new ToolCallReponse(allowd, result);
        }, _toolRegistry);

        var messages = new List<Message>();

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

        // 添加系统提示词
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(Message.FromSystem(systemPrompt));
        }

        // 添加历史消息
        if (context?.History != null)
        {
            messages.AddRange(context.History);
        }

        // 添加用户输入
        messages.Add(Message.FromUser(input));

        var totalUsage = new TokenUsage();
        var stopwatch = Stopwatch.StartNew();

        // 发送开始事件
        yield return new AgentStartEvent
        {
            AgentId = _config.Id,
            AgentName = _config.Name,
            Model = _config.Model
        };

        // Agent Loop
        for (int round = 1; round <= _config.MaxToolRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 发送轮次开始事件
            yield return new AgentRoundStartEvent
            {
                AgentId = _config.Id,
                Round = round
            };

            // 上下文压缩检查
            if (_contextManager != null)
            {
                var compactionResult = await _contextManager.CompactIfNeededAsync(
                    messages, cancellationToken);

                if (compactionResult != null)
                {
                    yield return new AgentContextCompactedEvent
                    {
                        AgentId = _config.Id,
                        Strategy = compactionResult.StrategyName,
                        PreCompactTokens = compactionResult.PreCompactTokens,
                        PostCompactTokens = compactionResult.PostCompactTokens,
                        PreCompactMessages = compactionResult.PreCompactMessages,
                        PostCompactMessages = compactionResult.PostCompactMessages,
                        CompactedMessages = compactionResult.RequestMessages
                    };
                }
            }

            // 构建 LLM 请求（动态注入已激活 Skill 的 Instructions）
            var requestMessages = messages.ToArray();
            if (!string.IsNullOrEmpty(_skillInstructions) && requestMessages.Length > 0 && requestMessages[0].Role == MessageRole.System)
            {
                // 更新系统消息，追加 Skill Instructions
                var updatedSystemMessage = Message.FromSystem(requestMessages[0].GetTextContent() + _skillInstructions);
                requestMessages = [updatedSystemMessage, .. requestMessages[1..]];
            }

            var request = new LlmRequest
            {
                Model = _config.Model,
                Messages = requestMessages,
                Tools = _toolRegistry.GetDefinitions(),
                Temperature = _config.Temperature,
                MaxTokens = _config.MaxTokens
            };

            // 调用 LLM 并转发流事件
            LlmResponse? response = null;
            var llmStream = _llmClient.Streaming(request);

            await foreach (var streamEvent in llmStream.WithCancellation(cancellationToken))
            {
                yield return new AgentLlmStreamEvent
                {
                    AgentId = _config.Id,
                    StreamEvent = streamEvent
                };
            }

            // 获取最终响应
            response = await llmStream.GetResponseAsync(cancellationToken);

            // 累计 token 用量
            if (response.Usage != null)
            {
                totalUsage = new TokenUsage
                {
                    InputTokens = totalUsage.InputTokens + response.Usage.InputTokens,
                    OutputTokens = totalUsage.OutputTokens + response.Usage.OutputTokens,
                    CacheHitTokens = totalUsage.CacheHitTokens + response.Usage.CacheHitTokens
                };
            }

            // 将助手消息加入对话历史
            var assistantMessage = new Message
            {
                Role = MessageRole.Assistant,
                Content = response.Content
            };
            messages.Add(assistantMessage);

            // 检查是否有工具调用
            var toolCalls = response.GetToolCalls();
            if (toolCalls.Length == 0)
            {
                // 无工具调用，Agent 完成
                stopwatch.Stop();

                // 触发 agent hooks（fire-and-forget，不阻塞）
                TriggerAgentRoundEndHooks(hookContext, round, messages, assistantMessage, cancellationToken);

                var result = new AgentResult
                {
                    Status = AgentStatus.Completed,
                    Message = assistantMessage,
                    Usage = totalUsage,
                    Rounds = round,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    EstimatedContextTokens = _contextManager?.EstimateTokens(messages) ?? 0,
                    MaxContextTokens = _contextManager?.MaxContextTokens ?? 0
                };

                yield return new AgentRoundEndEvent
                {
                    AgentId = _config.Id,
                    Round = round,
                    HasToolCalls = false
                };

                yield return new AgentCompleteEvent
                {
                    AgentId = _config.Id,
                    Result = result
                };

                await TriggerAgentSessionEndHooksAsync(hookContext, messages, cancellationToken);

                yield break;
            }

            // 有工具调用，执行工具
            yield return new AgentRoundEndEvent
            {
                AgentId = _config.Id,
                Round = round,
                HasToolCalls = true
            };

            // 执行工具
            if (_config.ParallelToolExecution && toolCalls.Length > 1)
            {
                await foreach (var evt in toolExecutor.ExecuteToolsParallelAsync(toolCalls,messages, cancellationToken))
                {
                    yield return evt;
                }
            }
            else
            {
                await foreach (var evt in toolExecutor.ExecuteToolsSequentialAsync(toolCalls, messages, cancellationToken))
                {
                    yield return evt;
                }
            }

            // 工具执行完成后，触发 agent hooks（fire-and-forget，不阻塞）
            TriggerAgentRoundEndHooks(hookContext, round, messages, assistantMessage, cancellationToken);
        }

        // 超过最大轮次，尝试让 LLM 生成最终回复
        // 添加提示让 LLM 总结当前结果
        var content = "You have reached the maximum number of tool rounds. " +
            "Please provide a final response to the user based on the information gathered so far. " +
            "Do not attempt to use any more tools." +
            "Do not generate <tool_call></tool_call> block.";
        messages.Add(Message.FromToolResult(
            toolCallId: Guid.NewGuid().ToString(),
            toolName: "max_iter_reached",
            content: [new TextBlock() { Text = content }]
         ));
        //messages.Add(Message.FromSystem(
        //    "You have reached the maximum number of tool rounds. " +
        //    "Please provide a final response to the user based on the information gathered so far. " +
        //    "Do not attempt to use any more tools." +
        //    "Do not generate <tool_call></tool_call> block."
        //));

        // 最后一次调用 LLM 获取总结
        var finalRequest = new LlmRequest
        {
            Model = _config.Model,
            Messages = messages.ToArray(),
            Tools = [],
            Temperature = 0,
            MaxTokens = _config.MaxTokens,
            ToolChoice = ToolChoiceMode.None
        };

        var finalStream = _llmClient.Streaming(finalRequest);
        await foreach (var streamEvent in finalStream.WithCancellation(cancellationToken))
        {
            yield return new AgentLlmStreamEvent
            {
                AgentId = _config.Id,
                StreamEvent = streamEvent
            };
        }

        var finalResponse = await finalStream.GetResponseAsync(cancellationToken);

        // 累计 token 用量
        if (finalResponse.Usage != null)
        {
            totalUsage = new TokenUsage
            {
                InputTokens = totalUsage.InputTokens + finalResponse.Usage.InputTokens,
                OutputTokens = totalUsage.OutputTokens + finalResponse.Usage.OutputTokens,
                CacheHitTokens = totalUsage.CacheHitTokens + finalResponse.Usage.CacheHitTokens
            };
        }

        var finalMessage = new Message
        {
            Role = MessageRole.Assistant,
            Content = finalResponse.Content
        };

        stopwatch.Stop();

        yield return new AgentCompleteEvent
        {
            AgentId = _config.Id,
            Result = new AgentResult
            {
                Status = AgentStatus.Completed,
                Message = finalMessage,
                Usage = totalUsage,
                Rounds = _config.MaxToolRounds,
                DurationMs = stopwatch.ElapsedMilliseconds,
                EstimatedContextTokens = _contextManager?.EstimateTokens(messages) ?? 0,
                MaxContextTokens = _contextManager?.MaxContextTokens ?? 0
            }
        };

        await TriggerAgentSessionEndHooksAsync(hookContext, messages, cancellationToken);
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
            toolResult = ToolResult.FromError("Tool execution denied by user.");
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
            if (evt is AgentCompleteEvent completeEvent)
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
