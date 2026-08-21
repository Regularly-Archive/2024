namespace InsightaAI.Agents.Subagents.Invocation;

/// <summary>
/// Explicit ownership and session linkage for one subagent invocation.
/// It never implies copying the parent conversation into the child.
/// </summary>
public sealed record SubagentInvocationContext
{
    /// <summary>Host-generated identity for this bounded child invocation.</summary>
    public string? InvocationId { get; init; }
    public string? UserId { get; init; }
    public string? SessionId { get; init; }
    public string? ParentSessionId { get; init; }
    public string? ParentInvocationId { get; init; }
}
