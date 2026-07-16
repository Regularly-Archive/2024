using InsightaAI.Agents.Orchestrator.Nodes;
using InsightaAI.Agents.Orchestrator.Storage;

namespace InsightaAI.Agents.Orchestrator.HumanInTheLoop;

/// <summary>
/// 任务审批上下文
/// </summary>
public sealed record TaskApprovalContext
{
    /// <summary>已完成的节点</summary>
    public required DAGNode Node { get; init; }

    /// <summary>节点执行结果</summary>
    public object? Result { get; init; }

    /// <summary>当前共享内存状态</summary>
    public required SharedMemory Memory { get; init; }
}

/// <summary>
/// 任务审批决策
/// </summary>
public enum TaskApprovalResult
{
    /// <summary>继续执行下一个</summary>
    Continue,

    /// <summary>暂停执行，等待人工干预</summary>
    Pause,

    /// <summary>终止整个编排</summary>
    Abort
}
