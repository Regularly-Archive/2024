using InsightaAI.LLM.Models;

namespace InsightaAI.Agents.Orchestrator.Results;

/// <summary>
/// 整个 Team 编排运行的结果
/// </summary>
public sealed record TeamResult
{
    /// <summary>运行状态</summary>
    public required TeamResultStatus Status { get; init; }

    /// <summary>所有节点的执行结果</summary>
    public required NodeResult[] NodeResults { get; init; }

    /// <summary>总 Token 用量</summary>
    public TokenUsage? TotalUsage { get; init; }

    /// <summary>总执行耗时（毫秒）</summary>
    public long TotalDurationMs { get; init; }

    /// <summary>最终输出（最后一个节点的输出）</summary>
    public string? FinalOutput { get; init; }

    /// <summary>错误信息（失败时）</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Team 运行状态
/// </summary>
public enum TeamResultStatus
{
    /// <summary>已完成</summary>
    Completed,

    /// <summary>失败</summary>
    Failed,

    /// <summary>已取消</summary>
    Cancelled,

    /// <summary>已中止</summary>
    Aborted
}
