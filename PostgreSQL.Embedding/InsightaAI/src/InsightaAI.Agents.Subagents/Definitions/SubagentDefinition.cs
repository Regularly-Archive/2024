namespace InsightaAI.Agents.Subagents.Definitions;

/// <summary>
/// Source-neutral description of a reusable or ephemeral subagent.
/// A catalog may persist it, while a DAG may provide one inline.
/// </summary>
public abstract record SubagentDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public abstract string AdapterKey { get; }
}
