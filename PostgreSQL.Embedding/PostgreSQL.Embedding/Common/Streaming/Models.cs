using System.Text.Json.Serialization;

namespace PostgreSQL.Embedding.Common.Streaming;

/// <summary>
/// Base interface for all SSE events
/// </summary>
public interface ISseEvent
{
    /// <summary>
    /// Event type for SSE 'event' field
    /// </summary>
    [JsonPropertyName("type")]
    string Type { get; }
}

/// <summary>
/// Message metadata
/// </summary>
public class MessageMetadata
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("type")]
    public string MessageType { get; set; } = "message";

    [JsonPropertyName("role")]
    public string Role { get; set; } = "assistant";

    [JsonPropertyName("content")]
    public List<ContentBlock> Content { get; set; } = new();

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }

    [JsonPropertyName("usage")]
    public UsageInfo? Usage { get; set; }

    [JsonPropertyName("context")]
    public ConversationContext Context { get; set; }


}

public class ConversationContext
{
    [JsonPropertyName("conversation_id")]
    public string ConversationId { get; set; }

    [JsonPropertyName("conversation_title")]
    public string ConversationTitle { get; set; }

    [JsonPropertyName("reference_message_id")]
    public string ReferenceMessageId { get; set; }
}

/// <summary>
/// Usage information
/// </summary>
public class UsageInfo
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }
}

/// <summary>
/// Content block (can be text, thinking, tool_use, etc.)
/// </summary>
public class ContentBlock
{
    [JsonPropertyName("type")]
    public string BlockType { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("thinking")]
    public string Thinking { get; set; } = "";

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("input")]
    public Dictionary<string, object>? Input { get; set; }

    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; set; }

    [JsonPropertyName("content")]
    public object? ToolContent { get; set; }

    [JsonPropertyName("is_error")]
    public bool IsError { get; set; }
}
