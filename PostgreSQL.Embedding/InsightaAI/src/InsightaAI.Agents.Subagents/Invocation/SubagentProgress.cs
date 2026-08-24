namespace InsightaAI.Agents.Subagents.Invocation;

/// <summary>Host-neutral progress reported while a subagent invocation is running.</summary>
public sealed record SubagentProgressUpdate
{
    public required SubagentProgressKind Kind { get; init; }
    public string? Message { get; init; }
    public string? Text { get; init; }
    public SubagentOutputStream? Stream { get; init; }
    public int? Round { get; init; }
    public string? ToolName { get; init; }
}

public enum SubagentProgressKind
{
    Started,
    Status,
    Output
}

/// <summary>Source stream for subagent tool output, when the update represents output.</summary>
public enum SubagentOutputStream
{
    Stdout,
    Stderr
}

/// <summary>Optional observer for execution-time subagent progress.</summary>
public interface ISubagentProgressReporter
{
    ValueTask ReportAsync(SubagentProgressUpdate update, CancellationToken cancellationToken = default);
}
