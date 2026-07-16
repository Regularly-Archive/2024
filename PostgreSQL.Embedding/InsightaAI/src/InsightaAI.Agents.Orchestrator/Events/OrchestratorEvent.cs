using InsightaAI.Agents.Orchestrator.Nodes;
using InsightaAI.Agents.Orchestrator.Results;

namespace InsightaAI.Agents.Orchestrator.Events;

/// <summary>
/// 编排器事件类型枚举
/// </summary>
public enum OrchestratorEventType
{
    PlanCreated,
    PlanApproved,
    PlanRejected,
    NodeStart,
    NodeComplete,
    NodeFailed,
    TaskApprovalRequested,
    BatchStart,
    BatchComplete,
    Complete,
    Error
}

/// <summary>
/// 编排器事件基类（遵循 AgentEvent 的 abstract record 模式）
/// </summary>
public abstract record OrchestratorEvent
{
    /// <summary>事件类型</summary>
    public abstract OrchestratorEventType Type { get; }

    /// <summary>事件时间戳</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>计划创建事件</summary>
public sealed record PlanCreatedEvent : OrchestratorEvent
{
    public override OrchestratorEventType Type => OrchestratorEventType.PlanCreated;
    public required DAGNode[] Nodes { get; init; }
    public string? Goal { get; init; }
}

/// <summary>计划审批通过事件</summary>
public sealed record PlanApprovedEvent : OrchestratorEvent
{
    public override OrchestratorEventType Type => OrchestratorEventType.PlanApproved;
}

/// <summary>计划拒绝事件</summary>
public sealed record PlanRejectedEvent : OrchestratorEvent
{
    public override OrchestratorEventType Type => OrchestratorEventType.PlanRejected;
    public string? Reason { get; init; }
}

/// <summary>节点开始执行事件</summary>
public sealed record NodeStartEvent : OrchestratorEvent
{
    public override OrchestratorEventType Type => OrchestratorEventType.NodeStart;
    public required string NodeId { get; init; }
    public required string NodeName { get; init; }
    public required NodeKind Kind { get; init; }
}

/// <summary>节点执行完成事件</summary>
public sealed record NodeCompleteEvent : OrchestratorEvent
{
    public override OrchestratorEventType Type => OrchestratorEventType.NodeComplete;
    public required NodeResult Result { get; init; }
}

/// <summary>节点执行失败事件</summary>
public sealed record NodeFailedEvent : OrchestratorEvent
{
    public override OrchestratorEventType Type => OrchestratorEventType.NodeFailed;
    public required string NodeId { get; init; }
    public required Exception Error { get; init; }
}

/// <summary>任务审批请求事件</summary>
public sealed record TaskApprovalRequestedEvent : OrchestratorEvent
{
    public override OrchestratorEventType Type => OrchestratorEventType.TaskApprovalRequested;
    public required DAGNode Node { get; init; }
    public object? Result { get; init; }
}

/// <summary>批次开始事件（并行执行的一组节点）</summary>
public sealed record BatchStartEvent : OrchestratorEvent
{
    public override OrchestratorEventType Type => OrchestratorEventType.BatchStart;
    public required string[] NodeIds { get; init; }
}

/// <summary>批次完成事件</summary>
public sealed record BatchCompleteEvent : OrchestratorEvent
{
    public override OrchestratorEventType Type => OrchestratorEventType.BatchComplete;
    public required string[] NodeIds { get; init; }
}

/// <summary>编排完成事件</summary>
public sealed record OrchestratorCompleteEvent : OrchestratorEvent
{
    public override OrchestratorEventType Type => OrchestratorEventType.Complete;
    public required TeamResult Result { get; init; }
}

/// <summary>编排错误事件</summary>
public sealed record OrchestratorErrorEvent : OrchestratorEvent
{
    public override OrchestratorEventType Type => OrchestratorEventType.Error;
    public required string Message { get; init; }
    public Exception? Error { get; init; }
}
