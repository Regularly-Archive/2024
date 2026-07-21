using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Context.Compaction;

/// <summary>
/// 微压缩策略 - 零成本清理旧工具结果
/// </summary>
/// <remarks>
/// 策略逻辑：
/// 1. 保留最近 N 个工具结果的完整内容
/// 2. 按 Full → Preview → Placeholder → Removed 逐级降低保留等级
/// 3. 工具只提供语义化投影，策略负责推进生命周期
/// 4. 删除时同时维护 ToolCall/ToolResult 配对结构
/// </remarks>
public sealed class MicroCompactStrategy : ICompactStrategy
{
    private readonly ToolRegistry? _toolRegistry;

    public string Name => "MicroCompact";
    public int Priority => 1; // 最高优先级

    public MicroCompactStrategy(ToolRegistry? toolRegistry = null)
    {
        _toolRegistry = toolRegistry;
    }

    public bool ShouldCompact(IReadOnlyList<Message> messages, int estimatedTokens, ContextBudget budget)
    {
        // 检查是否达到微压缩阈值
        if (estimatedTokens < budget.MicroCompactTriggerTokens)
            return false;

        // 各级阈值是可叠加的资格线；高压区仍应先尝试零成本的 MicroCompact。
        return HasCompactableToolResults(messages, estimatedTokens, budget);
    }

    public Task<CompactionResult> CompactAsync(
        List<Message> messages,
        ContextBudget budget,
        ITokenEstimator tokenEstimator,
        int preCompactTokens,
        CancellationToken cancellationToken = default)
    {
        var preCompactMessages = messages.Count;
        var toolPairs = FindToolPairs(messages);
        toolPairs.Reverse();
        var targetLevel = GetTargetLevel(preCompactTokens, budget);
        var removedToolCallIds = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < toolPairs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (_, toolResultIndex, toolName) = toolPairs[i];

            // 保留最近 N 个工具结果
            if (i < budget.KeepRecentToolResults)
                continue;

            var toolResultMessage = messages[toolResultIndex];
            var projector = _toolRegistry?.GetExecutor(toolName) as IToolResultProjector;
            var state = GetState(toolResultMessage, projector);
            var currentLevel = state.RetentionLevel;
            if (currentLevel >= targetLevel || currentLevel >= state.MinimumLevel)
                continue;

            var projectionContext = CreateProjectionContext(toolResultMessage, toolName, state);
            var currentTokens = EstimateContentTokens(toolResultMessage.Content, tokenEstimator);
            var maximumLevel = (ToolResultRetentionLevel)Math.Min((int)targetLevel, (int)state.MinimumLevel);

            // 保留有效的渐进降级；若某一级投影没有收益，则继续试探下一允许等级。
            for (var levelValue = (int)currentLevel + 1; levelValue <= (int)maximumLevel; levelValue++)
            {
                var candidateLevel = (ToolResultRetentionLevel)levelValue;
                if (candidateLevel == ToolResultRetentionLevel.Removed)
                {
                    if (!string.IsNullOrEmpty(toolResultMessage.ToolCallId))
                        removedToolCallIds.Add(toolResultMessage.ToolCallId);
                    break;
                }

                ToolResultProjection projection;
                if (candidateLevel == ToolResultRetentionLevel.Preview)
                {
                    var result = new ToolResult
                    {
                        Content = toolResultMessage.Content,
                        IsError = IsErrorMessage(toolResultMessage)
                    };
                    projection = (projector ?? DefaultMicroCompactProjector.Instance)
                        .CreatePreview(result, projectionContext);
                }
                else
                {
                    projection = (projector ?? DefaultMicroCompactProjector.Instance)
                        .CreatePlaceholder(projectionContext);
                }

                if (EstimateContentTokens(projection.Content, tokenEstimator) >= currentTokens)
                    continue;

                messages[toolResultIndex] = toolResultMessage with
                {
                    Content = projection.Content,
                    ToolResultState = state with { RetentionLevel = projection.Level }
                };
                break;
            }
        }

        RemoveToolPairs(messages, removedToolCallIds);

        // 估算压缩后的 token 数量
        var postCompactTokens = EstimateMessagesTokens(messages, tokenEstimator);

        return Task.FromResult(new CompactionResult
        {
            StrategyName = Name,
            PreCompactTokens = preCompactTokens,
            PostCompactTokens = postCompactTokens,
            PreCompactMessages = preCompactMessages,
            PostCompactMessages = messages.Count,
            RequestMessages = messages.ToArray()
        });
    }

    private static ToolResultRetentionLevel GetTargetLevel(int estimatedTokens, ContextBudget budget)
    {
        if (estimatedTokens >= budget.TraditionalCompactTriggerTokens)
            return ToolResultRetentionLevel.Removed;
        if (estimatedTokens >= budget.SessionCompactTriggerTokens)
            return ToolResultRetentionLevel.Placeholder;
        return ToolResultRetentionLevel.Preview;
    }

    private static ToolResultState GetState(Message message, IToolResultProjector? projector)
    {
        if (message.ToolResultState != null)
            return message.ToolResultState;

        var policy = projector?.RetentionPolicy ?? DefaultMicroCompactProjector.Policy;
        return new ToolResultState
        {
            RetentionLevel = ToolResultRetentionLevel.Full,
            OriginalLength = message.GetTextContent().Length,
            CanReplay = policy.CanReplay,
            HasSideEffects = policy.HasSideEffects,
            MinimumLevel = policy.MinimumLevel
        };
    }

    private static ToolResultProjectionContext CreateProjectionContext(
        Message message, string toolName, ToolResultState state)
    {
        var text = message.GetTextContent();
        return new ToolResultProjectionContext
        {
            ToolName = toolName,
            ToolCallId = message.ToolCallId ?? string.Empty,
            OriginalLength = state.OriginalLength > 0 ? state.OriginalLength : text.Length,
            OriginalLineCount = new Lazy<int>(() => text.Length == 0 ? 0 : text.Count(c => c == '\n') + 1),
            Artifact = state.Artifact
        };
    }

    private static void RemoveToolPairs(List<Message> messages, HashSet<string> toolCallIds)
    {
        if (toolCallIds.Count == 0)
            return;

        messages.RemoveAll(message => message.Role == MessageRole.ToolResult &&
            message.ToolCallId != null && toolCallIds.Contains(message.ToolCallId));

        // 一条 Assistant 消息可能包含多个并行 ToolCall。
        // 这里只删除命中的内容块，其他 ToolCall 和文本内容必须原样保留。
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var message = messages[i];
            if (message.Role != MessageRole.Assistant)
                continue;

            var content = message.Content
                .Where(block => block is not ToolCallBlock call || !toolCallIds.Contains(call.Id))
                .ToArray();
            if (content.Length == message.Content.Length)
                continue;
            if (content.Length == 0)
                messages.RemoveAt(i);
            else
                messages[i] = message with { Content = content };
        }
    }

    /// <summary>
    /// 查找所有 tool_use + tool_result 配对
    /// </summary>
    /// <returns>列表，每项包含 (toolUseIndex, toolResultIndex, toolName)</returns>
    private static List<(int ToolUseIndex, int ToolResultIndex, string ToolName)> FindToolPairs(List<Message> messages)
    {
        var pairs = new List<(int, int, string)>();

        for (int i = 0; i < messages.Count; i++)
        {
            var message = messages[i];

            // 查找包含 tool_use 的 Assistant 消息
            if (message.Role == MessageRole.Assistant)
            {
                foreach (var block in message.Content)
                {
                    if (block is ToolCallBlock toolCall)
                    {
                        // 查找对应的 tool_result
                        for (int j = i + 1; j < messages.Count; j++)
                        {
                            var potentialResult = messages[j];
                            if (potentialResult.Role == MessageRole.ToolResult &&
                                potentialResult.ToolCallId == toolCall.Id)
                            {
                                pairs.Add((i, j, toolCall.Name));
                                break;
                            }
                        }
                    }
                }
            }
        }

        return pairs;
    }

    /// <summary>
    /// 检查是否是错误消息
    /// </summary>
    private static bool IsErrorMessage(Message message)
    {
        return message.Content.OfType<TextBlock>()
            .Any(t => t.Text.Contains("error", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 创建截断上下文
    /// </summary>
    /// <summary>
    /// 检查是否有可压缩的工具结果
    /// </summary>
    private bool HasCompactableToolResults(
        IReadOnlyList<Message> messages, int estimatedTokens, ContextBudget budget)
    {
        int toolResultCount = 0;
        var targetLevel = GetTargetLevel(estimatedTokens, budget);

        for (int i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == MessageRole.ToolResult)
            {
                toolResultCount++;

                // 超过保留数量后，检查是否有可压缩的
                if (toolResultCount > budget.KeepRecentToolResults)
                {
                    var toolName = messages[i].ToolName ?? "";
                    var projector = _toolRegistry?.GetExecutor(toolName) as IToolResultProjector;
                    var state = GetState(messages[i], projector);
                    if (state.RetentionLevel < targetLevel && state.RetentionLevel < state.MinimumLevel)
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 估算消息列表的 token 数量
    /// </summary>
    private static int EstimateMessagesTokens(List<Message> messages, ITokenEstimator tokenEstimator)
    {
        int total = 0;
        foreach (var message in messages)
        {
            total += 4; // 消息开销
            foreach (var block in message.Content)
            {
                if (block is TextBlock textBlock)
                    total += tokenEstimator.EstimateTokens(textBlock.Text);
                else if (block is ThinkingBlock thinkingBlock)
                    total += tokenEstimator.EstimateTokens(thinkingBlock.Thinking);
                else if (block is ImageBlock)
                    total += 2000;
                else if (block is ToolCallBlock toolCall)
                {
                    total += tokenEstimator.EstimateTokens(toolCall.Name);
                    total += tokenEstimator.EstimateTokens(toolCall.Arguments.GetRawText());
                }
                else if (block is ToolResultBlock toolResult)
                {
                    foreach (var content in toolResult.Content)
                    {
                        if (content is TextBlock text)
                            total += tokenEstimator.EstimateTokens(text.Text);
                        else if (content is ImageBlock)
                            total += 2000;
                    }
                }
            }
        }
        return total;
    }

    private static int EstimateContentTokens(IEnumerable<ContentBlock> content, ITokenEstimator tokenEstimator)
    {
        var total = 0;
        foreach (var block in content)
        {
            total += block switch
            {
                TextBlock text => tokenEstimator.EstimateTokens(text.Text),
                ThinkingBlock thinking => tokenEstimator.EstimateTokens(thinking.Thinking),
                ImageBlock => 2000,
                ToolCallBlock call => tokenEstimator.EstimateTokens(call.Name) +
                    tokenEstimator.EstimateTokens(call.Arguments.GetRawText()),
                ToolResultBlock result => EstimateContentTokens(result.Content, tokenEstimator),
                _ => 0
            };
        }

        return total;
    }

    private sealed class DefaultMicroCompactProjector : IToolResultProjector
    {
        public static readonly DefaultMicroCompactProjector Instance = new();
        public static readonly ToolResultRetentionPolicy Policy = new();
        public ToolResultRetentionPolicy RetentionPolicy => Policy;

        public ToolResultProjection CreatePreview(ToolResult result, ToolResultProjectionContext context)
        {
            var text = string.Join("\n", result.Content.OfType<TextBlock>().Select(block => block.Text));
            var lines = text.Split('\n');
            var preview = string.Join("\n", lines.Take(50));
            if (lines.Length > 100)
                preview += $"\n\n[... omitted {lines.Length - 100} lines ...]\n\n" + string.Join("\n", lines.TakeLast(50));
            if (context.Artifact != null)
                preview += $"\n\n[Full output available as artifact {context.Artifact.Id}.]";
            return new ToolResultProjection
            {
                Content = [new TextBlock { Text = preview }],
                Level = ToolResultRetentionLevel.Preview
            };
        }

        public ToolResultProjection CreatePlaceholder(ToolResultProjectionContext context) => new()
        {
            Content =
            [
                new TextBlock
                {
                    Text = context.Artifact != null
                        ? $"[Previous {context.ToolName} result omitted. Full output is available as artifact {context.Artifact.Id} ({context.Artifact.Path}).]"
                        : $"[Previous {context.ToolName} result omitted. Re-run the tool if needed.]"
                }
            ],
            Level = ToolResultRetentionLevel.Placeholder
        };
    }
}
