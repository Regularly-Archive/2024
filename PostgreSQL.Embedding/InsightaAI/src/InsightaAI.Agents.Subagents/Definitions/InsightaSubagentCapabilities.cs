namespace InsightaAI.Agents.Subagents.Definitions;

/// <summary>
/// Declarative tool groups requested by an Insighta-runtime subagent.
/// Hosts map these requests to the Agent's effective tool set and may only reduce them.
/// </summary>
public sealed record InsightaSubagentCapabilities
{
    public static InsightaSubagentCapabilities RestrictedDefault { get; } = new();

    public bool EnableSkills { get; init; }
    public bool EnableMcp { get; init; }
    public bool EnableMemory { get; init; }
}
