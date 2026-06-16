using InsightaAI.Orchestrator.Nodes;

namespace InsightaAI.Orchestrator.Results;

/// <summary>
/// 单个 DAG 节点的执行结果
/// </summary>
public sealed record NodeResult
{
    /// <summary>节点 ID</summary>
    public required string NodeId { get; init; }

    /// <summary>节点名称</summary>
    public required string NodeName { get; init; }

    /// <summary>节点类型</summary>
    public required NodeKind NodeKind { get; init; }

    /// <summary>执行状态</summary>
    public required NodeResultStatus Status { get; init; }

    /// <summary>输出结果</summary>
    public object? Output { get; init; }

    /// <summary>错误信息（失败时）</summary>
    public string? Error { get; init; }

    /// <summary>执行耗时（毫秒）</summary>
    public long DurationMs { get; init; }
}

/// <summary>
/// 节点执行状态
/// </summary>
public enum NodeResultStatus
{
    /// <summary>成功</summary>
    Success,

    /// <summary>失败</summary>
    Failed,

    /// <summary>跳过（依赖失败）</summary>
    Skipped,

    /// <summary>取消</summary>
    Cancelled
}
