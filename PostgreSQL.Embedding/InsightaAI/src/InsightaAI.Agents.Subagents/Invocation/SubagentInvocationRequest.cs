using InsightaAI.Agents.Subagents.Definitions;

namespace InsightaAI.Agents.Subagents.Invocation;

/// <summary>One bounded request to a resolved subagent definition.</summary>
public sealed record SubagentInvocationRequest
{
    public required SubagentDefinition Definition { get; init; }
    public required string Input { get; init; }
    public SubagentInvocationContext Context { get; init; } = new();
    /// <summary>
    /// Optional host-side restriction. Internal adapters may only reduce their definition's tools;
    /// external adapters retain ownership of their own tool systems.
    /// </summary>
    public IReadOnlyList<string>? AllowedToolNames { get; init; }
    public ISubagentProgressReporter? Progress { get; init; }
}
