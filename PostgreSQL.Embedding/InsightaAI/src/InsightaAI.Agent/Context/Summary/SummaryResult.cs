using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Context.Summary;

public enum SummaryMode
{
    Full,
    Incremental
}

public sealed record SummaryResult
{
    public required bool Success { get; init; }

    public string? Summary { get; init; }

    public required SummaryMode Mode { get; init; }

    public required DoneReason FinishReason { get; init; }

    public int Attempts { get; init; }

    public string? Error { get; init; }
}
