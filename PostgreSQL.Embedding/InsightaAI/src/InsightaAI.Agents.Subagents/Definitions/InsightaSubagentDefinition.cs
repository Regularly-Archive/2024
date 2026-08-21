namespace InsightaAI.Agents.Subagents.Definitions;

/// <summary>
/// A subagent executed by Insighta's own Agent runtime. Host adapters resolve these fields
/// into their private Agent configuration and retain authority over security-sensitive settings.
/// </summary>
public sealed record InsightaSubagentDefinition : SubagentDefinition
{
    public const string Adapter = "insighta";

    public override string AdapterKey => Adapter;
    public string? Model { get; init; }
    public string Instructions { get; init; } = string.Empty;
    public int? MaxTokens { get; init; }
    public int? MaxToolRounds { get; init; }
    public IReadOnlyList<string> ToolNames { get; init; } = [];
    /// <summary>Whether project AGENTS.md is included in the child system prompt.</summary>
    public bool IncludeProjectInstructions { get; init; } = true;
    public InsightaSubagentCapabilities Capabilities { get; init; } = InsightaSubagentCapabilities.RestrictedDefault;
}
