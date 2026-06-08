using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Context;

/// <summary>
/// 上下文管理器接口
/// </summary>
public interface IContextManager
{
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
}
