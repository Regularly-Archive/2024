namespace InsightaAI.LLM.Models;

/// <summary>
/// Token 用量统计
/// </summary>
public sealed record TokenUsage
{
    /// <summary>输入 token 数</summary>
    public int InputTokens { get; init; }

    /// <summary>输出 token 数</summary>
    public int OutputTokens { get; init; }

    /// <summary>缓存读取 token 数</summary>
    public int CacheReadTokens { get; init; }

    /// <summary>缓存写入 token 数</summary>
    public int CacheWriteTokens { get; init; }

    /// <summary>总 token 数</summary>
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>预估费用 (美元)</summary>
    public decimal? EstimatedCost { get; init; }
}
