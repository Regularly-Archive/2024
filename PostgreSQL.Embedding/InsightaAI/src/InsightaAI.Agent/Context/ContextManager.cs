using System.Collections.Concurrent;
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

            // 按优先级检查策略
            foreach (var strategy in _strategies)
            {
                if (strategy.ShouldCompact(messages, estimatedTokens, _budget))
                {
                    var result = await strategy.CompactAsync(
                        messages, _budget, _tokenEstimator, estimatedTokens, cancellationToken);

                    return result;
                }
            }

            return null;
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

            ICompactStrategy? targetStrategy = strategy.ToLowerInvariant() switch
            {
                "micro" => _strategies.FirstOrDefault(s => s.Name == "MicroCompact"),
                "traditional" => _strategies.FirstOrDefault(s => s.Name == "TraditionalCompact"),
                "auto" => _strategies.FirstOrDefault(s => s.ShouldCompact(messages, estimatedTokens, _budget))
                    ?? _strategies.LastOrDefault(),
                _ => null
            };

            if (targetStrategy == null)
                return null;

            return await targetStrategy.CompactAsync(
                messages, _budget, _tokenEstimator, estimatedTokens, cancellationToken);
        }
        finally
        {
            _compactLock.Release();
        }
    }
}
