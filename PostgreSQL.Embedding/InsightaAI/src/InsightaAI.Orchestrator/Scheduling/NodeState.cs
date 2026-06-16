namespace InsightaAI.Orchestrator.Scheduling;

/// <summary>
/// 节点执行状态
/// </summary>
public enum NodeState
{
    /// <summary>等待中（依赖未满足）</summary>
    Pending,

    /// <summary>就绪（依赖已满足，可执行）</summary>
    Ready,

    /// <summary>执行中</summary>
    Running,

    /// <summary>已完成</summary>
    Completed,

    /// <summary>失败</summary>
    Failed,

    /// <summary>跳过（依赖失败）</summary>
    Skipped
}
