using System.Text.Json.Serialization;

namespace PostgreSQL.Embedding.Common.Streaming;

/// <summary>
/// Message start event - sent when message begins
/// </summary>
public class MessageStartEvent : ISseEvent
{
    [JsonPropertyName("type")]
    public string Type => "message_start";

    [JsonPropertyName("message")]
    public MessageMetadata Message { get; set; } = new();
}

/// <summary>
/// Ping event - heartbeat to keep connection alive
/// </summary>
public class PingEvent : ISseEvent
{
    [JsonPropertyName("type")]
    public string Type => "ping";
}

/// <summary>
/// Content block start event - indicates a new content block is beginning
/// </summary>
public class ContentBlockStartEvent : ISseEvent
{
    [JsonPropertyName("type")]
    public string Type => "content_block_start";

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("content_block")]
    public ContentBlock ContentBlock { get; set; } = new();
}

/// <summary>
/// Content block delta event - incremental update to content block
/// </summary>
public class ContentBlockDeltaEvent : ISseEvent
{
    [JsonPropertyName("type")]
    public string Type => "content_block_delta";

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("delta")]
    public ContentBlockDelta Delta { get; set; } = new();
}

/// <summary>
/// Incremental delta for content block
/// </summary>
public class ContentBlockDelta
{
    [JsonPropertyName("type")]
    public string DeltaType { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("thinking")]
    public string Thinking { get; set; } = "";

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = "";
}

/// <summary>
/// Content block stop event - indicates content block is complete
/// </summary>
public class ContentBlockStopEvent : ISseEvent
{
    [JsonPropertyName("type")]
    public string Type => "content_block_stop";

    [JsonPropertyName("index")]
    public int Index { get; set; }
}

/// <summary>
/// Message stop event - indicates message is complete
/// </summary>
public class MessageStopEvent : ISseEvent
{
    [JsonPropertyName("type")]
    public string Type => "message_stop";
}

/// <summary>
/// Message delta event - incremental update to message (includes usage and stop_reason)
/// </summary>
public class MessageDeltaEvent : ISseEvent
{
    [JsonPropertyName("type")]
    public string Type => "message_delta";

    [JsonPropertyName("delta")]
    public MessageDelta Delta { get; set; } = new();

    [JsonPropertyName("usage")]
    public UsageInfo? Usage { get; set; }
}

/// <summary>
/// Message delta details
/// </summary>
public class MessageDelta
{
    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }

    [JsonPropertyName("stop_sequence")]
    public string? StopSequence { get; set; }
}

/// <summary>
/// Citations event - contains reference information from RAG
/// Sent by plugins (e.g., RAGFlowPlugin) through the EventBus
/// </summary>
public class CitationsEvent : ISseEvent
{
    [JsonPropertyName("type")]
    public string Type => "citations";

    [JsonPropertyName("citations")]
    public List<CitationItem> Citations { get; set; } = new();
}

/// <summary>
/// Individual citation item with position information
/// </summary>
public class CitationItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// Positions where this citation appears in the text
    /// </summary>
    [JsonPropertyName("positions")]
    public List<CitationPosition> Positions { get; set; } = new();

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("relevance")]
    public float Relevance { get; set; }

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = "document"; // "document" | "web"
}

/// <summary>
/// Position where a citation appears in the text
/// </summary>
public class CitationPosition
{
    [JsonPropertyName("start_index")]
    public int StartIndex { get; set; }

    [JsonPropertyName("end_index")]
    public int EndIndex { get; set; }
}
