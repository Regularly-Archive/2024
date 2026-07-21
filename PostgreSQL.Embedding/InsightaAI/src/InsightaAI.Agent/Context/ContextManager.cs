using System.Collections.Concurrent;
using InsightaAI.Agent.Context.Compaction;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Context;

/// <summary>
/// 上下文管理器 - 协调压缩策略的执行
/// </summary>
public sealed class ContextManager : IContextManager
{
    private readonly SemaphoreSlim _compactLock = new(1, 1);
    private readonly List<ICompactStrategy> _strategies;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ContextBudget _budget;

    /// <summary>
    /// 创建上下文管理器
    /// </summary>
    /// <param name="tokenEstimator">Token 估算器</param>
    /// <param name="budget">上下文预算配置</param>
    /// <param name="strategies">压缩策略列表（可选，为空时使用默认策略）</param>
    public ContextManager(
        ITokenEstimator tokenEstimator,
        ContextBudget budget,
        IEnumerable<ICompactStrategy>? strategies = null)
    {
        _tokenEstimator = tokenEstimator ?? throw new ArgumentNullException(nameof(tokenEstimator));
        _budget = budget ?? throw new ArgumentNullException(nameof(budget));

        // 按优先级排序策略
        _strategies = strategies?.OrderBy(s => s.Priority).ToList()
            ?? new List<ICompactStrategy>();
    }

    /// <summary>
    /// 最大上下文窗口大小（token 数）
    /// </summary>
    public int MaxContextTokens => _budget.MaxContextTokens;

    public int AvailableInputTokens => _budget.AvailableInputTokens;

    /// <summary>
    /// 估算消息列表的 token 数量
    /// </summary>
    public int EstimateTokens(IReadOnlyList<Message> messages)
    {
        int total = 0;
        foreach (var message in messages)
        {
            // 消息开销
            total += 4;

            // 内容 token
            foreach (var block in message.Content)
            {
                switch (block)
                {
                    case TextBlock textBlock:
                        total += _tokenEstimator.EstimateTokens(textBlock.Text);
                        break;
                    case ThinkingBlock thinkingBlock:
                        total += _tokenEstimator.EstimateTokens(thinkingBlock.Thinking);
                        break;
                    case ImageBlock:
                        // 图片估算：保守取 2000 tokens（实际取决于分辨率）
                        total += 2000;
                        break;
                    case ToolCallBlock toolCall:
                        total += _tokenEstimator.EstimateTokens(toolCall.Name);
                        total += _tokenEstimator.EstimateTokens(toolCall.Arguments.GetRawText());
                        break;
                    case ToolResultBlock toolResult:
                        foreach (var content in toolResult.Content)
                        {
                            if (content is TextBlock text)
                                total += _tokenEstimator.EstimateTokens(text.Text);
                            else if (content is ImageBlock)
                                total += 2000; // 保守估算
                        }
                        break;
                }
            }
        }
        return total;
    }

    /// <summary>
    /// 检查是否需要压缩，如果需要则执行
    /// </summary>
    public async Task<CompactionResult?> CompactIfNeededAsync(
        List<Message> messages,
        CancellationToken cancellationToken = default)
    {
        if (!_budget.Enabled || _strategies.Count == 0)
            return null;

        // 确保同一时间只有一个压缩操作
        if (!await _compactLock.WaitAsync(0, cancellationToken))
            return null;

        try
        {
            var estimatedTokens = EstimateTokens(messages);
            var originalTokens = estimatedTokens;
            var originalMessages = messages.Count;
            var appliedStrategies = new List<string>();
            var restoredAttachments = new List<string>();
            Message? boundaryMarker = null;

            // 阈值是可叠加的资格线：每层执行后重新估算，仍超限则继续下一层。
            foreach (var strategy in _strategies)
            {
                if (strategy.ShouldCompact(messages, estimatedTokens, _budget))
                {
                    var result = await TryCompactAsync(
                        strategy, messages, estimatedTokens, cancellationToken);
                    if (result == null)
                        continue;

                    appliedStrategies.Add(strategy.Name);
                    restoredAttachments.AddRange(result.RestoredAttachments);
                    boundaryMarker = result.BoundaryMarker ?? boundaryMarker;
                    estimatedTokens = result.PostCompactTokens;
                }
            }

            if (appliedStrategies.Count == 0)
                return null;

            return new CompactionResult
            {
                StrategyName = string.Join("+", appliedStrategies),
                PreCompactTokens = originalTokens,
                PostCompactTokens = estimatedTokens,
                PreCompactMessages = originalMessages,
                PostCompactMessages = messages.Count,
                RequestMessages = messages.ToArray(),
                RestoredAttachments = restoredAttachments,
                BoundaryMarker = boundaryMarker
            };
        }
        finally
        {
            _compactLock.Release();
        }
    }

    /// <summary>
    /// 强制执行压缩
    /// </summary>
    public async Task<CompactionResult?> ForceCompactAsync(
        List<Message> messages,
        string strategy = "auto",
        CancellationToken cancellationToken = default)
    {
        if (!_budget.Enabled)
            return null;

        await _compactLock.WaitAsync(cancellationToken);
        try
        {
            var estimatedTokens = EstimateTokens(messages);

            if (strategy.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                // 手动 auto 不受自动触发阈值限制：按优先级试算，提交第一个真正产生收益的策略。
                foreach (var candidate in _strategies)
                {
                    var result = await TryCompactAsync(
                        candidate, messages, estimatedTokens, cancellationToken);
                    if (result != null)
                        return result;
                }

                return null;
            }

            ICompactStrategy? targetStrategy = strategy.ToLowerInvariant() switch
            {
                "micro" => _strategies.FirstOrDefault(s => s.Name == "MicroCompact"),
                "traditional" => _strategies.FirstOrDefault(s => s.Name == "TraditionalCompact"),
                "sessionmemory" => _strategies.FirstOrDefault(s => s.Name == "SessionMemoryCompact"),
                _ => null
            };

            if (targetStrategy == null)
                return null;

            return await TryCompactAsync(
                targetStrategy, messages, estimatedTokens, cancellationToken);
        }
        finally
        {
            _compactLock.Release();
        }
    }

    public ContextBudget GetContextBudget()
    {
        return _budget;
    }

    /// <summary>
    /// 在消息副本上试算压缩；只有实际 token 或消息数下降时才提交。
    /// </summary>
    private async Task<CompactionResult?> TryCompactAsync(
        ICompactStrategy strategy,
        List<Message> messages,
        int preCompactTokens,
        CancellationToken cancellationToken)
    {
        var preCompactMessages = messages.Count;
        var trialMessages = messages.ToList();
        var trialResult = await strategy.CompactAsync(
            trialMessages, _budget, _tokenEstimator, preCompactTokens, cancellationToken);
        var postCompactTokens = EstimateTokens(trialMessages);
        var postCompactMessages = trialMessages.Count;

        if (postCompactTokens >= preCompactTokens && postCompactMessages >= preCompactMessages)
            return null;

        messages.Clear();
        messages.AddRange(trialMessages);

        return trialResult with
        {
            PreCompactTokens = preCompactTokens,
            PostCompactTokens = postCompactTokens,
            PreCompactMessages = preCompactMessages,
            PostCompactMessages = postCompactMessages,
            RequestMessages = messages.ToArray()
        };
    }
}
