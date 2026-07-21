using InsightaAI.LLM.Abstractions;

namespace InsightaAI.Agent.Context.Summary;

public sealed record SummaryOptions
{
    public required string Model { get; init; }

    public required Func<string, ILlmClient> ClientFactory { get; init; }

    public int MaxTokens { get; init; } = 2048;

    public int TargetTokens { get; init; } = 1200;

    public double Temperature { get; init; } = 0.3;

    public int MaxAttempts { get; init; } = 2;

    public int TitleMaxTokens { get; init; } = 256;

    public int TitleMaxAttempts { get; init; } = 2;

    public int TitleMaxCharacters { get; init; } = 40;

    public int TitleFallbackMaxCharacters { get; init; } = 30;
}
