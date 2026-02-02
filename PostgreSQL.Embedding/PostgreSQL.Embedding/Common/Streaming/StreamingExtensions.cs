using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.AspNetCore.Http;

namespace PostgreSQL.Embedding.Common.Streaming;

/// <summary>
/// Extension methods for creating and writing SSE events
/// </summary>
public static class StreamingExtensions
{
    private const string LineEnding = "\r\n";
    private const string NewLine = "\r\n\r\n";
    private const string EventPrefix = "event: ";
    private const string IdPrefix = "id: ";
    private const string DataPrefix = "data: ";

    // JSON serializer options that preserve Chinese characters
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Configure HTTP response for SSE
    /// </summary>
    public static void ConfigureSseResponse(this HttpResponse response)
    {
        if (response.HasStarted) return;

        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["Connection"] = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";
    }

    /// <summary>
    /// Write an SSE event to the HTTP response
    /// </summary>
    public static async Task WriteSseEventAsync<TEvent>(
        this HttpResponse response,
        TEvent @event,
        CancellationToken ct = default) where TEvent : ISseEvent
    {
        await WriteEventCoreAsync(response, @event.Type, @event, ct);
    }

    /// <summary>
    /// Write raw text as SSE data
    /// </summary>
    public static async Task WriteSseDataAsync(
        this HttpResponse response,
        string data,
        string? eventType = null,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(eventType))
        {
            sb.Append($"{EventPrefix}{eventType}{NewLine}");
        }

        sb.Append($"{DataPrefix}{data}{NewLine}");

        await response.WriteAsync(sb.ToString(), Encoding.UTF8, ct);
        await response.Body.FlushAsync(ct);
    }

    /// <summary>
    /// Write SSE comment (for debugging)
    /// </summary>
    public static async Task WriteSseCommentAsync(
        this HttpResponse response,
        string comment,
        CancellationToken ct = default)
    {
        await response.WriteAsync($": {comment}{NewLine}", Encoding.UTF8, ct);
        await response.Body.FlushAsync(ct);
    }

    /// <summary>
    /// Core method to serialize and write SSE event
    /// </summary>
    private static async Task WriteEventCoreAsync<TEvent>(
        HttpResponse response,
        string eventType,
        TEvent @event,
        CancellationToken ct)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(eventType))
        {
            sb.Append($"{EventPrefix}{eventType}{LineEnding}");
        }

        // Serialize using concrete type, not interface type, with UnsafeRelaxedJsonEscaping to preserve Chinese
        var json = JsonSerializer.Serialize(@event, @event.GetType(), JsonOptions);
        sb.Append($"{DataPrefix}{json}{NewLine}");

        await response.WriteAsync(sb.ToString(), Encoding.UTF8, ct);
        await response.Body.FlushAsync(ct);
    }

    #region Event Creation Extensions

    /// <summary>
    /// Create a text content block start event
    /// </summary>
    public static ContentBlockStartEvent TextBlockStart(this ISseEvent _, int index) => new()
    {
        Index = index,
        ContentBlock = new ContentBlock { BlockType = "text" }
    };

    /// <summary>
    /// Create a text delta event
    /// </summary>
    public static ContentBlockDeltaEvent TextDelta(this ISseEvent _, int index, string text) => new()
    {
        Index = index,
        Delta = new ContentBlockDelta { DeltaType = "text_delta", Text = text }
    };

    /// <summary>
    /// Create a thinking content block start event
    /// </summary>
    public static ContentBlockStartEvent ThinkingBlockStart(this ISseEvent _, int index) => new()
    {
        Index = index,
        ContentBlock = new ContentBlock { BlockType = "thinking", Thinking = "" }
    };

    /// <summary>
    /// Create a thinking delta event
    /// </summary>
    public static ContentBlockDeltaEvent ThinkingDelta(this ISseEvent _, int index, string thinking) => new()
    {
        Index = index,
        Delta = new ContentBlockDelta { DeltaType = "thinking_delta", Thinking = thinking }
    };

    /// <summary>
    /// Create a thinking signature delta event
    /// </summary>
    public static ContentBlockDeltaEvent ThinkingSignature(this ISseEvent _, int index, string signature) => new()
    {
        Index = index,
        Delta = new ContentBlockDelta { DeltaType = "signature_delta", Signature = signature }
    };

    /// <summary>
    /// Create a content block stop event
    /// </summary>
    public static ContentBlockStopEvent BlockStop(this ISseEvent _, int index) => new()
    {
        Index = index
    };

    /// <summary>
    /// Create a tool use event
    /// </summary>
    public static ToolUseEvent ToolUse(this ISseEvent _, string name, Dictionary<string, object> input) => new()
    {
        Name = name,
        Input = input
    };

    /// <summary>
    /// Create a tool result event
    /// </summary>
    public static ToolResultEvent ToolResult(this ISseEvent _, string toolUseId, object content, bool isError = false) => new()
    {
        ToolUseId = toolUseId,
        Content = content,
        IsError = isError
    };

    /// <summary>
    /// Create a combined tool call event
    /// </summary>
    public static ToolCallEvent ToolCall(this ISseEvent _, string name, Dictionary<string, object> input) => new()
    {
        Name = name,
        Input = input,
        Status = "pending"
    };

    /// <summary>
    /// Create a message start event
    /// </summary>
    public static MessageStartEvent MessageStart(this ISseEvent _, string model = "") => new()
    {
        Message = new MessageMetadata
        {
            Id = Guid.NewGuid().ToString("N"),
            Role = "assistant",
            Content = new List<ContentBlock>(),
            Model = model
        }
    };

    /// <summary>
    /// Create a message stop event
    /// </summary>
    public static MessageStopEvent MessageStop(this ISseEvent _) => new();

    /// <summary>
    /// Create a ping event
    /// </summary>
    public static PingEvent Ping(this ISseEvent _) => new();

    /// <summary>
    /// Create an error event
    /// </summary>
    public static ErrorEvent Error(this ISseEvent _, string message, string errorType = "internal_error") => new()
    {
        Error = new ErrorDetails
        {
            ErrorType = errorType,
            Message = message
        }
    };

    #endregion
}
