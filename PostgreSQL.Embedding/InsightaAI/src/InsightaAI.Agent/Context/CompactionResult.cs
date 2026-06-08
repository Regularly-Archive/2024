using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Context;

/// <summary>
/// 压缩结果
/// </summary>
public sealed record CompactionResult
{
    /// <summary>
    /// 使用的策略名称
    /// </summary>
    public required string StrategyName { get; init; }

    /// <summary>
    /// 压缩前的 token 数
    /// </summary>
    public int PreCompactTokens { get; init; }

    /// <summary>
    /// 压缩后的 token 数
    /// </summary>
    public int PostCompactTokens { get; init; }

    /// <summary>
    /// 压缩前的消息数
    /// </summary>
    public int PreCompactMessages { get; init; }

    /// <summary>
    /// 压缩后的消息数
    /// </summary>
    public int PostCompactMessages { get; init; }

    /// <summary>
    /// 恢复的附件列表
    /// </summary>
    public List<string> RestoredAttachments { get; init; } = [];

    /// <summary>
    /// 压缩后用于 LLM 请求的消息数组
    /// </summary>
    public required Message[] RequestMessages { get; init; }

    /// <summary>
    /// 压缩边界标记消息（可选，用于 TraditionalCompact）
    /// </summary>
    public Message? BoundaryMarker { get; init; }

    /// <summary>
    /// 压缩节省的 token 数
    /// </summary>
    public int TokensSaved => PreCompactTokens - PostCompactTokens;

    /// <summary>
    /// 压缩率（0-1）
    /// </summary>
    public double CompressionRatio => PreCompactTokens > 0
        ? 1.0 - (double)PostCompactTokens / PreCompactTokens
        : 0;
}
