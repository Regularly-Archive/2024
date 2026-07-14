using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Models;
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
    private readonly Func<string> _getSkillInstructions;

    public AgentLoop(
        AgentConfig config,
        ILlmClient llmClient,
        ToolRegistry toolRegistry,
        ToolCallExecutor toolCallExecutor,
        Func<string> getSkillInstructions)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(llmClient);
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(toolCallExecutor);
        ArgumentNullException.ThrowIfNull(getSkillInstructions);

        _config = config;
        _llmClient = llmClient;
        _toolRegistry = toolRegistry;
        _toolCallExecutor = toolCallExecutor;
        _getSkillInstructions = getSkillInstructions;
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

            // 构建 LLM 请求（动态注入已激活 Skill 的 Instructions）
            var requestMessages = context.Messages.ToArray();
            var skillInstructions = _getSkillInstructions();
            if (!string.IsNullOrEmpty(skillInstructions) && requestMessages.Length > 0 && requestMessages[0].Role == MessageRole.System)
            {
                var updatedSystemMessage = Message.FromSystem(requestMessages[0].GetTextContent() + skillInstructions);
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
            var response = await llmStream.GetResponseAsync(cancellationToken);

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
            context.AddMessage(assistantMessage);

            // 检查是否有工具调用（去重：LLM 可能生成相同名称+参数的重复调用）
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

                yield return new AgentCompleteEvent
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
                        MaxContextTokens = context.MaxContextTokens
                    }
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

            // 将工具执行结果加入对话历史
            foreach (var result in _toolCallExecutor.Results)
            {
                context.AddMessage(new Message
                {
                    Role = MessageRole.ToolResult,
                    ToolCallId = result.ToolCall.Id,
                    ToolName = result.ToolCall.Name,
                    Content = result.Result.Content,
                    ToolResultIntercepted = result.Intercepted
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
        // 添加提示让 LLM 总结当前结果
        var content = "You have reached the maximum number of tool rounds. " +
            "Please provide a final response to the user based on the information gathered so far. " +
            "Do not attempt to use any more tools." +
            "Do not generate <tool_call></tool_call> block.";
        context.AddMessage(Message.FromToolResult(
            toolCallId: Guid.NewGuid().ToString(),
            toolName: "max_iter_reached",
            content: [new TextBlock() { Text = content }]
         ));

        // 最后一次调用 LLM 获取总结
        var finalRequest = new LlmRequest
        {
            Model = _config.Model,
            Messages = context.Messages.ToArray(),
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
                EstimatedContextTokens = context.EstimateTokens(),
                MaxContextTokens = context.MaxContextTokens
            }
        };
    }

    /// <summary>
    /// 去重工具调用：LLM 可能生成多个名称和参数完全相同的工具调用
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
