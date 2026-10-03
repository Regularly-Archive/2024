using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Prompts;
using InsightaAI.Agent.Tools;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace InsightaAI.Agent;

/// <summary>
/// Agent 核心循环 — 负责 LLM 调用、工具执行、消息累积
/// 不关心 Hook 触发、System Prompt 构建、消息持久化等基础设施
/// </summary>
public sealed class AgentLoop
{
    /// <summary>单轮内截断续写的最大次数（防病态循环；正常 1-5 轮即完成）。</summary>
    private const int MaxContinuations = 16;

    /// <summary>连续无可见正文增量（no-progress）的最大次数，超过即止损放弃。</summary>
    private const int MaxNoProgressRounds = 2;

    private readonly AgentConfig _config;
    private readonly ILlmClient _llmClient;
    private readonly ToolRegistry _toolRegistry;
    private readonly ToolCallExecutor _toolCallExecutor;
    private readonly Func<CancellationToken, Task<string>> _systemPromptBuilder;
    private readonly ILogger? _logger;

    public AgentLoop(
        AgentConfig config,
        ILlmClient llmClient,
        ToolRegistry toolRegistry,
        ToolCallExecutor toolCallExecutor,
        Func<CancellationToken, Task<string>> buildSystemPrompt,
        ILogger? logger = null)
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
        _logger = logger;
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
                MaxTokens = _config.MaxTokens,
                ReasoningPreference = _config.ReasoningPreference
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
            totalUsage = AccumulateUsage(totalUsage, response.Usage);

            // 构造助手消息（截断续写终结后再入历史，保证历史中是一条完整消息）
            var assistantMessage = new Message
            {
                Role = MessageRole.Assistant,
                Content = response.Content
            };

            // 检查是否有工具调用（去重：LLM 流可能重复发出同一工具名和原始参数的调用）
            var toolCalls = DeduplicateToolCalls(response.GetToolCalls());

            // 截断标记独立于收尾开关：只要可见正文被输出上限截断就如实标记，
            // 开关只控制是否续写。收尾管线终结后按最终响应更新。
            var wasTruncated = toolCalls.Length == 0 && IsTruncatedDelivery(response);
            if (toolCalls.Length == 0 && _config.EnableOutputContinuation
                && (!HasVisibleText(response.Content) || response.FinishReason == DoneReason.MaxTokens))
            {
                // 文本收尾管线：截断续写与空文本指令重试，全部在入历史前的请求级投影内消化，
                // 不消耗工具循环轮次，最终只合并为一条助手消息。
                var draft = new StringBuilder();
                AppendVisibleText(draft, response.Content);

                var outcome = new TextFinalizeOutcome { FinalResponse = response };
                await foreach (var evt in FinalizeTextResponseAsync(context, draft, outcome, cancellationToken))
                {
                    yield return evt;
                }

                if (outcome.Failed)
                {
                    // 收尾请求失败：与主循环错误路径一致（草稿不入历史，回合标记失败）
                    stopwatch.Stop();
                    yield return CreateFailedTurnEndEvent(context, totalUsage, stopwatch, round, outcome.ErrorMessage!);
                    yield break;
                }

                totalUsage = AccumulateUsage(totalUsage, outcome.Usage);
                response = outcome.FinalResponse!;
                wasTruncated = IsTruncatedDelivery(response);
                if (wasTruncated)
                {
                    _logger?.LogWarning(
                        "Agent {AgentId} output still truncated after finalization rounds (max {Max} continuations).",
                        _config.Id, MaxContinuations);
                }

                // 有累积正文 → 合并为一条助手消息（正文 + 最后响应的非文本块）；全空 → 原样入历史
                if (draft.Length > 0)
                {
                    assistantMessage = MergeContinuationMessage(draft.ToString(), response.Content);
                }
            }

            await context.AddMessageAsync(assistantMessage);

            if (toolCalls.Length == 0)
            {
                yield return CreateRoundEndEvent(round, hasToolCalls: false);

                // 空文本守卫：模型可能以纯 thinking（或空内容）结束回复——典型原因是推理
                // 阶段耗尽输出预算被截断。仅记 Warning 日志，不向用户报错。
                if (!HasVisibleText(response.Content))
                {
                    if (_config.EnableOutputContinuation)
                    {
                        // 收尾管线已做过指令重试（含零增长止损）仍无正文：止损收束，
                        // 不再消耗主循环轮次空转——空消息已入历史，下一轮请求只会重演。
                        _logger?.LogWarning(
                            "Round {Round} of agent {AgentId} ended without visible text after finalization retries; finalizing with empty message.",
                            round, _config.Id);

                        stopwatch.Stop();
                        yield return CreateCompletedTurnEndEvent(
                            context, totalUsage, stopwatch, round, assistantMessage, wasTruncated);
                        yield break;
                    }

                    // 开关关闭：保持既有行为——还有剩余轮次就继续循环让模型补写正文；
                    // 已到最大轮次则落入 HandleMaxRoundsExceededAsync 强制收尾。
                    _logger?.LogWarning(
                        "Round {Round} of agent {AgentId} ended without visible text (thinking-only response, possible output truncation). {Action}",
                        round, _config.Id,
                        round < _config.MaxToolRounds
                            ? "Continuing with next round."
                            : "Falling back to max-rounds final answer.");

                    if (round < _config.MaxToolRounds)
                    {
                        continue;
                    }

                    break;
                }

                // 无工具调用，Agent 完成
                stopwatch.Stop();
                yield return CreateCompletedTurnEndEvent(
                    context, totalUsage, stopwatch, round, assistantMessage, wasTruncated);
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

            yield return CreateRoundEndEvent(round, hasToolCalls: true);

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
            ToolChoice = ToolChoiceMode.None,
            ReasoningPreference = _config.ReasoningPreference
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
                AvailableInputTokens = context.AvailableInputTokens,
                WasTruncated = finalResponse.FinishReason == DoneReason.MaxTokens
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

    /// <summary>构造成功完成的回合结束事件（正常文本完成与止损收束共用）。</summary>
    private static AgentTurnEndEvent CreateCompletedTurnEndEvent(
        ILoopContext context, TokenUsage usage, Stopwatch stopwatch, int round,
        Message assistantMessage, bool wasTruncated) => new()
        {
            AgentId = context.AgentId,
            Result = new AgentResult
            {
                Status = AgentStatus.Completed,
                Message = assistantMessage,
                Usage = usage,
                Rounds = round,
                DurationMs = stopwatch.ElapsedMilliseconds,
                EstimatedContextTokens = context.EstimateTokens(),
                MaxContextTokens = context.MaxContextTokens,
                AvailableInputTokens = context.AvailableInputTokens,
                WasTruncated = wasTruncated
            }
        };

    private AgentRoundEndEvent CreateRoundEndEvent(int round, bool hasToolCalls) => new()
    {
        AgentId = _config.Id,
        Round = round,
        HasToolCalls = hasToolCalls
    };

    /// <summary>判断响应是否构成截断交付：可见正文被输出上限截断（空响应不算截断）。</summary>
    private static bool IsTruncatedDelivery(LlmResponse response) =>
        response.FinishReason == DoneReason.MaxTokens && HasVisibleText(response.Content);

    /// <summary>累加增量用量；增量为 null（流式未上报）时原样返回。</summary>
    private static TokenUsage AccumulateUsage(TokenUsage total, TokenUsage? increment)
    {
        if (increment == null)
        {
            return total;
        }

        return new TokenUsage
        {
            InputTokens = total.InputTokens + increment.InputTokens,
            OutputTokens = total.OutputTokens + increment.OutputTokens,
            CacheHitTokens = total.CacheHitTokens + increment.CacheHitTokens
        };
    }

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

    /// <summary>判断响应是否包含非空白正文文本（thinking 与工具调用不算正文）。</summary>
    private static bool HasVisibleText(IEnumerable<ContentBlock> content) =>
        content.OfType<TextBlock>().Any(b => !string.IsNullOrWhiteSpace(b.Text));

    /// <summary>
    /// 文本收尾循环的结果载体（IAsyncEnumerable 方法无法直接返回值）。
    /// </summary>
    private sealed class TextFinalizeOutcome
    {
        /// <summary>进入收尾前的初始响应，随每次迭代更新为最近一次响应。</summary>
        public LlmResponse? FinalResponse { get; set; }

        /// <summary>收尾期间累计的 token 用量（不含初始响应）。</summary>
        public TokenUsage? Usage { get; set; }

        /// <summary>收尾请求是否失败。</summary>
        public bool Failed { get; set; }

        /// <summary>失败时的错误信息。</summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 文本收尾是否继续：无可见正文增量（no-progress）连续超限即止损；
    /// 草稿为空时继续注入指令要求模型直接输出；草稿被截断时继续续写。
    /// </summary>
    private static bool ShouldContinueFinalization(
        LlmResponse response, StringBuilder draft, int noProgressRounds, int continuations)
    {
        if (noProgressRounds >= MaxNoProgressRounds)
        {
            return false;
        }

        if (draft.Length == 0)
        {
            return true;
        }

        return response.FinishReason == DoneReason.MaxTokens && continuations < MaxContinuations;
    }

    /// <summary>
    /// 文本收尾循环：在请求级快照上累积草稿，处理截断续写与空文本响应，直到模型正常结束、
    /// 达到续写上限或连续 no-progress 止损。草稿为空时注入指令要求模型直接输出正文（不前置
    /// assistant 块）；草稿非空时以 assistant 草稿 + 续写指令延续。每次迭代的流事件实时转发给
    /// 消费者；结果（最终响应 / 用量 / 失败状态）写入 <paramref name="outcome"/>。
    /// </summary>
    private async IAsyncEnumerable<AgentEvent> FinalizeTextResponseAsync(
        ILoopContext context,
        StringBuilder draft,
        TextFinalizeOutcome outcome,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var usage = new TokenUsage();
        var continuations = 0;
        var noProgressRounds = 0;
        string? continuationPrompt = null;
        string? emptyTextPrompt = null;

        while (ShouldContinueFinalization(outcome.FinalResponse!, draft, noProgressRounds, continuations))
        {
            continuations++;

            var snapshot = context.Messages.ToList();
            var isEmptyTextRound = draft.Length == 0;
            if (isEmptyTextRound)
            {
                _logger?.LogInformation(
                    "Agent {AgentId} produced no visible text, requesting direct output ({NoProgress}/{Max}).",
                    _config.Id, noProgressRounds + 1, MaxNoProgressRounds);
                emptyTextPrompt ??= await PromptTemplate.RenderAsync("continue-after-empty");
            }
            else
            {
                _logger?.LogInformation(
                    "Agent {AgentId} output hit max tokens, continuing generation ({Count}/{Max}).",
                    _config.Id, continuations, MaxContinuations);
                snapshot.Add(new Message
                {
                    Role = MessageRole.Assistant,
                    Content = [new TextBlock { Text = draft.ToString() }]
                });
                continuationPrompt ??= await PromptTemplate.RenderAsync("continue-after-truncation");
            }

            snapshot.Add(Message.FromUser(isEmptyTextRound ? emptyTextPrompt! : continuationPrompt!));

            // 收尾是纯文本延续：不带工具，避免中途发起工具调用导致草稿悬空
            var finalizeRequest = new LlmRequest
            {
                Model = _config.Model,
                Messages = snapshot.ToArray(),
                Tools = [],
                Temperature = _config.Temperature,
                MaxTokens = _config.MaxTokens,
                ReasoningPreference = _config.ReasoningPreference
            };

            var finalizeStream = _llmClient.Streaming(finalizeRequest);
            ErrorEvent? finalizeError = null;

            await foreach (var streamEvent in finalizeStream.WithCancellation(cancellationToken))
            {
                if (streamEvent is ErrorEvent errorEvent)
                {
                    finalizeError = errorEvent;
                    yield return CreateAgentErrorEvent(errorEvent);
                    continue;
                }

                yield return new AgentLlmStreamEvent
                {
                    AgentId = _config.Id,
                    StreamEvent = streamEvent
                };
            }

            var finalizeResponse = await finalizeStream.GetResponseAsync(cancellationToken);

            if (finalizeError != null)
            {
                outcome.Failed = true;
                outcome.ErrorMessage = finalizeError.Error.Message;
                yield break;
            }

            if (finalizeResponse.Usage != null)
            {
                usage = AccumulateUsage(usage, finalizeResponse.Usage);
            }

            var lengthBefore = draft.Length;
            AppendVisibleText(draft, finalizeResponse.Content);
            noProgressRounds = draft.Length == lengthBefore ? noProgressRounds + 1 : 0;
            outcome.FinalResponse = finalizeResponse;
        }

        outcome.Usage = usage;
    }

    /// <summary>把响应中的可见正文文本追加到累积草稿。</summary>
    private static void AppendVisibleText(StringBuilder draft, ContentBlock[] content)
    {
        foreach (var text in content.OfType<TextBlock>())
        {
            draft.Append(text.Text);
        }
    }

    /// <summary>
    /// 合并截断续写结果为一条助手消息：累积正文文本 + 最后一次响应中的非文本块（thinking 等）。
    /// </summary>
    private static Message MergeContinuationMessage(string draftText, ContentBlock[] lastContent)
    {
        var blocks = new List<ContentBlock>(lastContent.Length + 1);
        blocks.AddRange(lastContent.Where(b => b is not TextBlock));
        blocks.Add(new TextBlock { Text = draftText });
        return new Message { Role = MessageRole.Assistant, Content = blocks.ToArray() };
    }
}
