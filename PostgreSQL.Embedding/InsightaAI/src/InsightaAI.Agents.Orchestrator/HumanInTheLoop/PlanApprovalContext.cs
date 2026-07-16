using InsightaAI.Agents.Orchestrator.Nodes;
using InsightaAI.Agents.Orchestrator.Storage;

namespace InsightaAI.Agents.Orchestrator.HumanInTheLoop;

/// <summary>
/// 计划审批上下文
/// </summary>
public sealed record PlanApprovalContext
{
    /// <summary>DAG 节点列表</summary>
    public required DAGNode[] Nodes { get; init; }

    /// <summary>原始目标（RunTeamAsync 时）</summary>
    public string? Goal { get; init; }

    /// <summary>当前共享内存状态</summary>
    public required SharedMemory Memory { get; init; }
}

/// <summary>
/// 计划审批结果
/// </summary>
public sealed record PlanApprovalResult
{
    /// <summary>是否批准</summary>
    public bool Approved { get; init; }

    /// <summary>可选的修改后的节点（用户可以在执行前修改 DAG）</summary>
    public DAGNode[]? ModifiedNodes { get; init; }
}
