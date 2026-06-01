using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Models;
using InsightaAI.LLM;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent;

/// <summary>
/// Agent 运行时 - 实现 Agent Loop 模式
/// </summary>
public class Agent
{
    private readonly AgentConfig _config;
    private readonly ILlmClient _llmClient;
    private readonly ToolRegistry _toolRegistry;
    private readonly List<IToolHook> _hooks = [];
    private readonly HashSet<string> _alwaysAllowedTools = [];

    /// <summary>
    /// 创建 Agent 实例
    /// </summary>
    public Agent(AgentConfig config, ILlmClient llmClient, ToolRegistry toolRegistry)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(llmClient);
        ArgumentNullException.ThrowIfNull(toolRegistry);

        _config = config;
        _llmClient = llmClient;
        _toolRegistry = toolRegistry;
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
        _hooks.Add(hook);
        return this;
    }

    /// <summary>
    /// 执行钩子检查
    /// </summary>
    private async Task<bool> CheckHooksAsync(
        string toolName,
        string arguments,
        ToolExecutionContext context)
    {
        // 如果工具已被标记为始终允许，跳过检查
        if (_alwaysAllowedTools.Contains(toolName))
        {
            return true;
        }

        foreach (var hook in _hooks)
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
        var conversationId = context?.ConversationId ?? Guid.NewGuid().ToString("N");
        var messages = new List<Message>();

        // 添加系统提示词
        if (!string.IsNullOrEmpty(_config.SystemPrompt))
        {
            messages.Add(Message.FromSystem(_config.SystemPrompt));
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

            // 构建 LLM 请求
            var request = new LlmRequest
            {
                Model = _config.Model,
                Messages = messages.ToArray(),
                Tools = _toolRegistry.GetDefinitions(),
                Temperature = _config.Temperature,
                MaxTokens = _config.MaxTokens
            };

            // 调用 LLM 并转发流事件
            LlmResponse? response = null;
            var llmStream = _llmClient.Stream(request);

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
                    OutputTokens = totalUsage.OutputTokens + response.Usage.OutputTokens
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

                var result = new AgentResult
                {
                    Status = AgentStatus.Completed,
                    Message = assistantMessage,
                    Usage = totalUsage,
                    Rounds = round,
                    DurationMs = stopwatch.ElapsedMilliseconds
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

                yield break;
            }

            // 有工具调用，执行工具
            yield return new AgentRoundEndEvent
            {
                AgentId = _config.Id,
                Round = round,
                HasToolCalls = true
            };

            // 判断是否并行执行
            if (_config.ParallelToolExecution && toolCalls.Length > 1)
            {
                // 并行执行多个工具调用
                var toolEvents = Channel.CreateUnbounded<AgentEvent>();
                var toolResults = new List<(ToolCallBlock ToolCall, ToolResult Result)>();
                var toolResultsLock = new object();

                var tasks = toolCalls.Select(async toolCall =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var arguments = toolCall.Arguments.GetRawText();

                    // 发送工具开始事件
                    await toolEvents.Writer.WriteAsync(new AgentToolStartEvent
                    {
                        AgentId = _config.Id,
                        ToolCallId = toolCall.Id,
                        ToolName = toolCall.Name,
                        Arguments = arguments
                    }, cancellationToken);

                    var toolContext = new ToolExecutionContext
                    {
                        AgentId = _config.Id,
                        ToolCallId = toolCall.Id,
                        ConversationId = conversationId,
                        CancellationToken = cancellationToken
                    };

                    // 检查钩子
                    var allowed = await CheckHooksAsync(toolCall.Name, arguments, toolContext);

                    ToolResult toolResult;
                    if (allowed)
                    {
                        // 执行工具
                        toolResult = await _toolRegistry.ExecuteAsync(toolCall, toolContext);
                    }
                    else
                    {
                        // 用户拒绝执行
                        toolResult = ToolResult.FromError("Tool execution denied by user.");
                    }

                    // 发送工具完成事件
                    var resultText = toolResult.Content.OfType<TextBlock>().FirstOrDefault()?.Text;
                    await toolEvents.Writer.WriteAsync(new AgentToolEndEvent
                    {
                        AgentId = _config.Id,
                        ToolCallId = toolCall.Id,
                        ToolName = toolCall.Name,
                        IsError = toolResult.IsError,
                        ResultPreview = resultText?.Length > 100 ? resultText[..100] + "..." : resultText
                    }, cancellationToken);

                    // 收集结果
                    lock (toolResultsLock)
                    {
                        toolResults.Add((toolCall, toolResult));
                    }
                }).ToArray();

                // 关闭 channel 当所有任务完成时
                _ = Task.WhenAll(tasks).ContinueWith(_ => toolEvents.Writer.Complete());

                // 转发事件
                await foreach (var evt in toolEvents.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return evt;
                }

                // 等待所有任务完成
                await Task.WhenAll(tasks);

                // 按原始顺序添加工具结果到对话历史
                foreach (var (toolCall, toolResult) in toolResults)
                {
                    var toolResultMessage = new Message
                    {
                        Role = MessageRole.ToolResult,
                        ToolCallId = toolCall.Id,
                        ToolName = toolCall.Name,
                        Content = toolResult.Content
                    };
                    messages.Add(toolResultMessage);
                }
            }
            else
            {
                // 顺序执行工具调用
                foreach (var toolCall in toolCalls)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var toolContext = new ToolExecutionContext
                    {
                        AgentId = _config.Id,
                        ToolCallId = toolCall.Id,
                        ConversationId = conversationId,
                        CancellationToken = cancellationToken
                    };

                    var arguments = toolCall.Arguments.GetRawText();

                    // 发送工具开始事件
                    yield return new AgentToolStartEvent
                    {
                        AgentId = _config.Id,
                        ToolCallId = toolCall.Id,
                        ToolName = toolCall.Name,
                        Arguments = arguments
                    };

                    // 检查钩子
                    var allowed = await CheckHooksAsync(toolCall.Name, arguments, toolContext);

                    ToolResult toolResult;
                    if (allowed)
                    {
                        // 执行工具
                        toolResult = await _toolRegistry.ExecuteAsync(toolCall, toolContext);
                    }
                    else
                    {
                        // 用户拒绝执行
                        toolResult = ToolResult.FromError("Tool execution denied by user.");
                    }

                    // 发送工具完成事件
                    var resultText = toolResult.Content.OfType<TextBlock>().FirstOrDefault()?.Text;
                    yield return new AgentToolEndEvent
                    {
                        AgentId = _config.Id,
                        ToolCallId = toolCall.Id,
                        ToolName = toolCall.Name,
                        IsError = toolResult.IsError,
                        ResultPreview = resultText?.Length > 100 ? resultText[..100] + "..." : resultText
                    };

                    // 将工具结果加入对话历史
                    var toolResultMessage = new Message
                    {
                        Role = MessageRole.ToolResult,
                        ToolCallId = toolCall.Id,
                        ToolName = toolCall.Name,
                        Content = toolResult.Content
                    };
                    messages.Add(toolResultMessage);
                }
            }
        }

        // 超过最大轮次，尝试让 LLM 生成最终回复
        // 添加提示让 LLM 总结当前结果
        messages.Add(Message.FromSystem(
            "You have reached the maximum number of tool rounds. " +
            "Please provide a final response to the user based on the information gathered so far. " +
            "Do not attempt to use any more tools."));

        // 最后一次调用 LLM 获取总结
        var finalRequest = new LlmRequest
        {
            Model = _config.Model,
            Messages = messages.ToArray(),
            Tools = [],  // 不提供工具，强制生成文本
            Temperature = _config.Temperature,
            MaxTokens = _config.MaxTokens
        };

        var finalStream = _llmClient.Stream(finalRequest);
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
                OutputTokens = totalUsage.OutputTokens + finalResponse.Usage.OutputTokens
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
                DurationMs = stopwatch.ElapsedMilliseconds
            }
        };
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
}
