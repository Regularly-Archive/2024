namespace InsightaAI.Agents.Subagents.Invocation;

public enum SubagentInvocationStatus
{
    Completed,
    Failed,
    Cancelled
}

/// <summary>Host-neutral result of one subagent invocation.</summary>
public sealed record SubagentInvocationResult
{
    public required string InvocationId { get; init; }
    public required SubagentInvocationStatus Status { get; init; }
    public string? SessionId { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
}
