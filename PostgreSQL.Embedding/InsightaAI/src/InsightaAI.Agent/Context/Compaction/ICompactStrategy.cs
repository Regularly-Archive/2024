using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Context.Compaction;

/// <summary>
/// 压缩策略接口
/// </summary>
public interface ICompactStrategy
{
    /// <summary>
    /// 策略名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 策略优先级（数字越小优先级越高）
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 检查是否需要执行此策略
    /// </summary>
    /// <param name="messages">当前消息列表</param>
    /// <param name="estimatedTokens">估算的 token 数量</param>
    /// <param name="budget">上下文预算配置</param>
    /// <returns>是否需要压缩</returns>
    bool ShouldCompact(IReadOnlyList<Message> messages, int estimatedTokens, ContextBudget budget);

    /// <summary>
    /// 执行压缩
    /// </summary>
    /// <param name="messages">原始消息列表（会被修改）</param>
    /// <param name="budget">上下文预算配置</param>
    /// <param name="tokenEstimator">Token 估算器</param>
    /// <param name="preCompactTokens">压缩前的 token 数量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩结果</returns>
    Task<CompactionResult> CompactAsync(
        List<Message> messages,
        ContextBudget budget,
        ITokenEstimator tokenEstimator,
        int preCompactTokens,
        CancellationToken cancellationToken = default);
}
