using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Prompts;
using InsightaAI.Agent.Tools;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace InsightaAI.Agent;

/// <summary>
/// Agent 核心循环 — 负责 LLM 调用、工具执行、消息累积
/// 不关心 Hook 触发、System Prompt 构建、消息持久化等基础设施
/// </summary>
public sealed class AgentLoop
{
    private readonly AgentConfig _config;
    private readonly ILlmClient _llmClient;
    private readonly ToolRegistry _toolRegistry;
    private readonly ToolCallExecutor _toolCallExecutor;
    private readonly Func<CancellationToken, Task<string>> _systemPromptBuilder;

    public AgentLoop(
        AgentConfig config,
        ILlmClient llmClient,
        ToolRegistry toolRegistry,
        ToolCallExecutor toolCallExecutor,
        Func<CancellationToken, Task<string>> buildSystemPrompt)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(llmClient);
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(toolCallExecutor);
        ArgumentNullException.ThrowIfNull(buildSystemPrompt);

        _config = config;
        _llmClient = llmClient;
        _toolRegistry = toolRegistry;
        _toolCallExecutor = toolCallExecutor;
        _systemPromptBuilder = buildSystemPrompt;
    }

    /// <summary>
    /// 运行 Agent Loop
    /// </summary>
    /// <param name="context">运行时上下文（已包含 system prompt + history + user input）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async IAsyncEnumerable<AgentEvent> RunAsync(
        ILoopContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var totalUsage = new TokenUsage();
        var stopwatch = Stopwatch.StartNew();

        // 发送开始事件
        yield return new AgentTurnStartEvent
        {
            AgentId = _config.Id,
            AgentName = _config.Name,
            Model = _config.Model
        };

        // Agent Loop
        for (int round = 1; round <= _config.MaxToolRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 上下文压缩检查
            var compactionResult = await context.CompactIfNeededAsync(cancellationToken);
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

            // 每轮重建 System Prompt（反映最新的 Skills 激活、Memory 等动态状态）
            if (context.Messages.Count > 0 && context.Messages[0].Role == MessageRole.System)
            {
                var rebuilt = await _systemPromptBuilder(cancellationToken);
                context.ReplaceMessage(0, Message.FromSystem(rebuilt));
            }
            var requestMessages = context.Messages.ToArray();

            // 仅在最终 LLM 输入已确定后发送轮次开始事件。
            yield return new AgentRoundStartEvent
            {
                AgentId = _config.Id,
                Round = round
            };

            var request = new LlmRequest
            {
                Model = _config.Model,
                Messages = requestMessages,
                Tools = _toolRegistry.GetDefinitions(),
                Temperature = _config.Temperature,
                MaxTokens = _config.MaxTokens
            };

            // 调用 LLM 并转发流事件
            var llmStream = _llmClient.Streaming(request);
            ErrorEvent? llmError = null;

            await foreach (var streamEvent in llmStream.WithCancellation(cancellationToken))
            {
                if (streamEvent is ErrorEvent errorEvent)
                {
                    llmError = errorEvent;
                    yield return CreateAgentErrorEvent(errorEvent);
                    continue;
                }

                yield return new AgentLlmStreamEvent
                {
                    AgentId = _config.Id,
                    StreamEvent = streamEvent
                };
            }

            // 获取最终响应
            var response = await llmStream.GetResponseAsync(cancellationToken);

            if (llmError != null || response.FinishReason == DoneReason.Error)
            {
                stopwatch.Stop();
                var error = llmError ?? CreateFallbackError();
                if (llmError == null)
                    yield return CreateAgentErrorEvent(error);

                yield return CreateFailedTurnEndEvent(context, totalUsage, stopwatch, round, error.Error.Message);
                yield break;
            }

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
            await context.AddMessageAsync(assistantMessage);

            // 检查是否有工具调用（去重：LLM 流可能重复发出同一工具名和原始参数的调用）
            var toolCalls = DeduplicateToolCalls(response.GetToolCalls());
            if (toolCalls.Length == 0)
            {
                // 无工具调用，Agent 完成
                stopwatch.Stop();

                yield return new AgentRoundEndEvent
                {
                    AgentId = _config.Id,
                    Round = round,
                    HasToolCalls = false
                };

                yield return new AgentTurnEndEvent
                {
                    AgentId = _config.Id,
                    Result = new AgentResult
                    {
                        Status = AgentStatus.Completed,
                        Message = assistantMessage,
                        Usage = totalUsage,
                        Rounds = round,
                        DurationMs = stopwatch.ElapsedMilliseconds,
                        EstimatedContextTokens = context.EstimateTokens(),
                        MaxContextTokens = context.MaxContextTokens,
                        AvailableInputTokens = context.AvailableInputTokens
                    }
                };

                yield break;
            }

            // 执行工具
            if (_config.ParallelToolExecution && toolCalls.Length > 1)
            {
                await foreach (var evt in _toolCallExecutor.ExecuteToolsParallelAsync(toolCalls, cancellationToken))
                {
                    yield return evt;
                }
            }
            else
            {
                await foreach (var evt in _toolCallExecutor.ExecuteToolsSequentialAsync(toolCalls, cancellationToken))
                {
                    yield return evt;
                }
            }

            yield return new AgentRoundEndEvent
            {
                AgentId = _config.Id,
                Round = round,
                HasToolCalls = true
            };

            // 将工具执行结果加入对话历史
            foreach (var result in _toolCallExecutor.Results)
            {
                await context.AddMessageAsync(new Message
                {
                    Role = MessageRole.ToolResult,
                    ToolCallId = result.ToolCall.Id,
                    ToolName = result.ToolCall.Name,
                    Content = result.Result.Content,
                    ToolResultState = result.State
                });
            }
        }

        // 超过最大轮次，让 LLM 生成最终回复
        await foreach (var evt in HandleMaxRoundsExceededAsync(context, totalUsage, stopwatch, cancellationToken))
        {
            yield return evt;
        }
    }

    /// <summary>
    /// 处理超过最大轮次的情况
    /// </summary>
    private async IAsyncEnumerable<AgentEvent> HandleMaxRoundsExceededAsync(
        ILoopContext context,
        TokenUsage totalUsage,
        Stopwatch stopwatch,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {

        var snapshot = context.Messages.ToList();
        var prompt = await PromptTemplate.RenderAsync("reached-max-rounds");
        snapshot.Add(Message.FromUser(prompt));

        // 最后一次调用 LLM 获取总结
        var finalRequest = new LlmRequest
        {
            Model = _config.Model,
            Messages = snapshot.ToArray(),
            Tools = [],
            Temperature = 0,
            MaxTokens = _config.MaxTokens,
            ToolChoice = ToolChoiceMode.None
        };

        var finalStream = _llmClient.Streaming(finalRequest);
        ErrorEvent? finalError = null;
        await foreach (var streamEvent in finalStream.WithCancellation(cancellationToken))
        {
            if (streamEvent is ErrorEvent errorEvent)
            {
                finalError = errorEvent;
                yield return CreateAgentErrorEvent(errorEvent);
                continue;
            }

            yield return new AgentLlmStreamEvent
            {
                AgentId = _config.Id,
                StreamEvent = streamEvent
            };
        }

        var finalResponse = await finalStream.GetResponseAsync(cancellationToken);

        if (finalError != null || finalResponse.FinishReason == DoneReason.Error)
        {
            stopwatch.Stop();
            var error = finalError ?? CreateFallbackError();
            if (finalError == null)
                yield return CreateAgentErrorEvent(error);

            yield return CreateFailedTurnEndEvent(context, totalUsage, stopwatch,
                _config.MaxToolRounds, error.Error.Message);
            yield break;
        }

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

        // 添加最后一条助手消息
        await context.AddMessageAsync(finalMessage);
        stopwatch.Stop();

        yield return new AgentTurnEndEvent
        {
            AgentId = _config.Id,
            Result = new AgentResult
            {
                Status = AgentStatus.Completed,
                Message = finalMessage,
                Usage = totalUsage,
                Rounds = _config.MaxToolRounds,
                DurationMs = stopwatch.ElapsedMilliseconds,
                EstimatedContextTokens = context.EstimateTokens(),
                MaxContextTokens = context.MaxContextTokens,
                AvailableInputTokens = context.AvailableInputTokens
            }
        };
    }

    /// <summary>将 LLM 流错误映射为 Agent 级错误事件。</summary>
    private AgentErrorEvent CreateAgentErrorEvent(ErrorEvent errorEvent) => new()
    {
        AgentId = _config.Id,
        ErrorMessage = errorEvent.Error.Message,
        Recoverable = errorEvent.Recoverable
    };

    private static ErrorEvent CreateFallbackError() => new()
    {
        Error = new InvalidOperationException("LLM stream completed with an error."),
        Recoverable = false
    };

    private static AgentTurnEndEvent CreateFailedTurnEndEvent(
        ILoopContext context, TokenUsage usage, Stopwatch stopwatch, int round, string error) => new()
        {
            AgentId = context.AgentId,
            Result = new AgentResult
            {
                Status = AgentStatus.Failed,
                Error = error,
                Usage = usage,
                Rounds = round,
                DurationMs = stopwatch.ElapsedMilliseconds,
                EstimatedContextTokens = context.EstimateTokens(),
                MaxContextTokens = context.MaxContextTokens,
                AvailableInputTokens = context.AvailableInputTokens
            }
        };

    /// <summary>
    /// 按工具名及原始 JSON 参数文本移除重复的工具调用。
    /// </summary>
    internal static ToolCallBlock[] DeduplicateToolCalls(ToolCallBlock[] toolCalls)
    {
        if (toolCalls.Length <= 1) return toolCalls;

        var seen = new HashSet<string>();
        var result = new List<ToolCallBlock>(toolCalls.Length);

        foreach (var tc in toolCalls)
        {
            var key = $"{tc.Name}:{tc.Arguments.GetRawText()}";
            if (seen.Add(key))
            {
                result.Add(tc);
            }
        }

        return result.Count == toolCalls.Length ? toolCalls : result.ToArray();
    }
}
