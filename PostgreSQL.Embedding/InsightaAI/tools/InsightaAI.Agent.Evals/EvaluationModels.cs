using System.Text.Json.Serialization;

namespace InsightaAI.Agent.Evals;

public sealed class EvaluationScenario
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = "";

    [JsonPropertyName("denyRules")]
    public List<EvaluationDenyRule> DenyRules { get; init; } = [];

    /// <summary>Tools a simulated permission hook approves for the whole session before the run.</summary>
    [JsonPropertyName("alwaysAllowTools")]
    public List<string> AlwaysAllowTools { get; init; } = [];

    /// <summary>Persists messages through JsonlMessageStorage for restored-session assertions.</summary>
    [JsonPropertyName("useMessageStorage")]
    public bool UseMessageStorage { get; init; }

    /// <summary>Overrides the context window to trigger real compaction inside the Agent Loop.</summary>
    [JsonPropertyName("maxContextTokens")]
    public int? MaxContextTokens { get; init; }

    /// <summary>Registers a DelegateTool whose stub handler returns this text; proves PreferPersistence on delegation.</summary>
    [JsonPropertyName("delegateResult")]
    public string? DelegateResult { get; init; }

    /// <summary>Optional fixture directory under the runner's copied Fixtures output.</summary>
    [JsonPropertyName("fixture")]
    public string? Fixture { get; init; }

    [JsonPropertyName("tools")]
    public List<EvaluationTool> Tools { get; init; } = [];

    [JsonPropertyName("steps")]
    public List<RecordedLlmStep> Steps { get; init; } = [];

    [JsonPropertyName("assert")]
    public EvaluationAssertions Assertions { get; init; } = new();
}

public sealed class EvaluationTool
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("result")]
    public string Result { get; init; } = "";

    /// <summary>Repeats result deterministically for large-output retention scenarios.</summary>
    [JsonPropertyName("repeat")]
    public int Repeat { get; init; } = 1;
}

public sealed class EvaluationDenyRule
{
    [JsonPropertyName("pattern")]
    public string Pattern { get; init; } = "";

    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "exact";
}

public sealed class RecordedLlmStep
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("toolCalls")]
    public List<RecordedToolCall> ToolCalls { get; init; } = [];
}

public sealed class RecordedToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("arguments")]
    public Dictionary<string, object?> Arguments { get; init; } = [];
}

public sealed class EvaluationAssertions
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "completed";

    [JsonPropertyName("toolCalls")]
    public List<string> ToolCalls { get; init; } = [];

    [JsonPropertyName("finalText")]
    public string? FinalText { get; init; }

    [JsonPropertyName("files")]
    public List<EvaluationFileAssertion> Files { get; init; } = [];

    /// <summary>Text which must be visible in the public Agent event stream.</summary>
    [JsonPropertyName("requiredEventText")]
    public List<string> RequiredEventText { get; init; } = [];

    /// <summary>Text which must never appear in the public Agent event stream.</summary>
    [JsonPropertyName("forbiddenEventText")]
    public List<string> ForbiddenEventText { get; init; } = [];

    /// <summary>Text which must be present in the redacted persisted tool result.</summary>
    [JsonPropertyName("requiredArtifactText")]
    public List<string> RequiredArtifactText { get; init; } = [];

    /// <summary>Text which must not be present in the redacted persisted tool result.</summary>
    [JsonPropertyName("forbiddenArtifactText")]
    public List<string> ForbiddenArtifactText { get; init; } = [];

    [JsonPropertyName("artifactTools")]
    public List<string> ArtifactTools { get; init; } = [];

    /// <summary>Assertions on artifacts read back from restored message storage.</summary>
    [JsonPropertyName("restoredArtifact")]
    public EvaluationRestoredArtifactAssertion? RestoredArtifact { get; init; }

    [JsonPropertyName("toolErrors")]
    public List<string> ToolErrors { get; init; } = [];

    /// <summary>Expected compaction strategy substring when a compacted event must fire.</summary>
    [JsonPropertyName("compactStrategy")]
    public string? CompactStrategy { get; init; }

    /// <summary>Upper bound for post-compact tokens; diagnostic sanity gate.</summary>
    [JsonPropertyName("maxPostCompactTokens")]
    public int? MaxPostCompactTokens { get; init; }
}

public sealed class EvaluationRestoredArtifactAssertion
{
    [JsonPropertyName("contains")]
    public List<string> Contains { get; init; } = [];

    [JsonPropertyName("notContains")]
    public List<string> NotContains { get; init; } = [];
}

public sealed class EvaluationFileAssertion
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("contains")]
    public List<string> Contains { get; init; } = [];

    [JsonPropertyName("notContains")]
    public List<string> NotContains { get; init; } = [];
}

public sealed class EvaluationReport
{
    public required string ScenarioId { get; init; }
    public required bool Passed { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required long DurationMs { get; init; }
    public required EvaluationMetrics Metrics { get; init; }
    public required IReadOnlyList<string> Failures { get; init; }
}

public sealed class EvaluationMetrics
{
    public required int LlmRequests { get; init; }
    public required int Turns { get; init; }
    public required int Rounds { get; init; }
    public required int ToolCalls { get; init; }
    public required int ToolErrors { get; init; }
    public required int AgentErrors { get; init; }
}
