namespace InsightaAI.Agent.Security;

/// <summary>
/// Removes secrets from tool-result text before it can be persisted or exposed to the model.
/// </summary>
public interface ISecretRedactor
{
    RedactionResult Redact(string content, RedactionContext context);
}

/// <summary>Context used to select format-aware redaction rules.</summary>
public sealed record RedactionContext
{
    public required string ToolName { get; init; }
    public string? SourcePath { get; init; }
    public SecretContentFormat Format { get; init; } = SecretContentFormat.Unknown;
}

/// <summary>Content formats supported by format-aware redactors.</summary>
public enum SecretContentFormat
{
    Unknown,
    Json,
    Yaml,
    Xml,
    Ini,
    DotEnv,
    PlainText
}

/// <summary>Result of redacting one text payload.</summary>
public sealed record RedactionResult
{
    public required string Content { get; init; }
    public bool WasRedacted { get; init; }
    public int RedactionCount { get; init; }
    public IReadOnlyList<RedactionFinding> Findings { get; init; } = [];
}

/// <summary>
/// Non-sensitive metadata about a redaction. It must never include the secret value.
/// </summary>
public sealed record RedactionFinding(string Category, string? Location);
