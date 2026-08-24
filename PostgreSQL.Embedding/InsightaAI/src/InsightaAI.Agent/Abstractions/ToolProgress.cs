namespace InsightaAI.Agent.Abstractions;

/// <summary>Describes an execution-time update reported by a tool.</summary>
public sealed record ToolProgressUpdate
{
    public required ToolProgressKind Kind { get; init; }
    public string? Message { get; init; }
    public string? Text { get; init; }
    public ToolOutputStream? Stream { get; init; }
}

public enum ToolProgressKind
{
    Status,
    Output,
    Heartbeat
}

public enum ToolOutputStream
{
    Stdout,
    Stderr
}

/// <summary>
/// Best-effort execution-time reporting channel supplied by the Agent runtime.
/// Tools report raw facts only; presentation policy belongs to the consuming UI.
/// </summary>
public interface IToolProgressReporter
{
    ValueTask ReportAsync(
        ToolProgressUpdate update,
        CancellationToken cancellationToken = default);
}

/// <summary>Default reporter used by tools outside an Agent execution pipeline.</summary>
public sealed class NullToolProgressReporter : IToolProgressReporter
{
    public static NullToolProgressReporter Instance { get; } = new();

    private NullToolProgressReporter() { }

    public ValueTask ReportAsync(ToolProgressUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        return ValueTask.CompletedTask;
    }
}
