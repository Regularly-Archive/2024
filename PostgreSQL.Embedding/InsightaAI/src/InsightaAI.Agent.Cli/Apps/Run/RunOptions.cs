using InsightaAI.Agent.Models;

namespace InsightaAI.Agent.Cli.Run;

/// <summary>A non-interactive run accepted by <see cref="RunApplication"/>.</summary>
public sealed record RunRequest
{
    public required string Input { get; init; }
    public string? SessionId { get; init; }
    public string? ProfileId { get; init; }

    /// <summary>Temporary narrowing for a profile. It can only remove tools from the profile.</summary>
    public IReadOnlyList<string>? AllowedToolNames { get; init; }
}

/// <summary>The process-level outcome of a run. JSONL events carry the detailed diagnostics.</summary>
public sealed record RunExecutionResult
{
    public required int ExitCode { get; init; }
    public string? SessionId { get; init; }
    public AgentStatus? Status { get; init; }
}
