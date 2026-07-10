using InsightaAI.Agent.Context.Compaction;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Context;

/// <summary>
/// 上下文管理器接口
/// </summary>
public interface IContextManager
{
    /// <summary>
    /// 最大上下文窗口大小（token 数）
    /// </summary>
    int MaxContextTokens { get; }

    /// <summary>
    /// 估算消息列表的 token 数量
    /// </summary>
    int EstimateTokens(IReadOnlyList<Message> messages);

    /// <summary>
    /// 检查是否需要压缩，如果需要则执行
    /// </summary>
    Task<CompactionResult?> CompactIfNeededAsync(
        List<Message> messages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 强制执行压缩
    /// </summary>
    Task<CompactionResult?> ForceCompactAsync(
        List<Message> messages,
        string strategy = "auto",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取上下文预算
    /// </summary>
    /// <returns></returns>
    ContextBudget GetContextBudget();
}
