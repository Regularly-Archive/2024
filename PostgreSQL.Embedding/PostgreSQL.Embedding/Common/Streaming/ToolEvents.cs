using System.Text.Json.Serialization;

namespace PostgreSQL.Embedding.Common.Streaming;

/// <summary>
/// Tool use event - indicates agent wants to call a tool/function
/// </summary>
public class ToolUseEvent : ISseEvent
{
    [JsonPropertyName("type")]
    public string Type => "tool_use";

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("input")]
    public Dictionary<string, object> Input { get; set; } = new();
}

/// <summary>
/// Planning event - emitted during task planning phase
/// Maps to StepTrace.Type == "Plan"
/// </summary>
public class PlanningEvent : ISseEvent
{
    [JsonPropertyName("type")]
    public string Type => "task";

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; }
}

/// <summary>
/// Tool result event - result from tool execution
/// </summary>
public class ToolResultEvent : ISseEvent
{
    [JsonPropertyName("type")]
    public string Type => "tool_result";

    [JsonPropertyName("tool_use_id")]
    public string ToolUseId { get; set; } = "";

    [JsonPropertyName("content")]
    public object Content { get; set; } = "";

    [JsonPropertyName("is_error")]
    public bool IsError { get; set; }
}

/// <summary>
/// Tool call event wrapper for compatibility
/// Combines tool_use and tool_result in one event
/// </summary>
public class ToolCallEvent : ISseEvent
{
    [JsonPropertyName("type")]
    public string Type => "tool_call";

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("input")]
    public Dictionary<string, object> Input { get; set; } = new();

    [JsonPropertyName("output")]
    public string? Output { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending"; // pending, running, completed, error

    [JsonPropertyName("duration_ms")]
    public long? DurationMs { get; set; }
}

/// <summary>
/// Error event
/// </summary>
public class ErrorEvent : ISseEvent
{
    [JsonPropertyName("type")]
    public string Type => "error";

    [JsonPropertyName("error")]
    public ErrorDetails Error { get; set; } = new();
}

/// <summary>
/// Error details
/// </summary>
public class ErrorDetails
{
    [JsonPropertyName("type")]
    public string ErrorType { get; set; } = "internal_error";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
