using System.Text.Json;
using System.Text.Json.Serialization;
using SqlSugar;

namespace InsightaAI.Agent.Storage;

/// <summary>
/// 会话记录
/// </summary>
[SugarTable("session_records")]
public class SessionRecord
{
    [SugarColumn(IsPrimaryKey = true, Length = 50)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    [SugarColumn(IsNullable = true, Length = 500)]
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [SugarColumn(Length = 100)]
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [SugarColumn(Length = 50)]
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "";

    [SugarColumn(IsNullable = true, Length = 100)]
    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [SugarColumn(IsNullable = true, Length = 500)]
    [JsonPropertyName("work_dir")]
    public string? WorkDir { get; set; }

    [JsonPropertyName("message_count")]
    public int MessageCount { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 消息记录 - 多态内容块
/// </summary>
[SugarTable("message_records")]
public class MessageRecord
{
    [SugarColumn(IsPrimaryKey = true, Length = 50)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>所属会话 ID（JSONL 序列化时忽略，由存储层管理）</summary>
    [SugarColumn(Length = 50, IsNullable = true)]
    [JsonIgnore]
    public string? SessionId { get; set; }

    [SugarColumn(Length = 20)]
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    /// <summary>内容块 JSON（PostgreSQL 用 jsonb 存储，JSONL 直接序列化）</summary>
    [SugarColumn(ColumnDataType = "jsonb", IsNullable = true)]
    [JsonPropertyName("content")]
    public List<ContentItem> Content { get; set; } = [];

    [SugarColumn(IsNullable = true, Length = 100)]
    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }

    [SugarColumn(IsNullable = true, Length = 100)]
    [JsonPropertyName("tool_name")]
    public string? ToolName { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 内容块基类（多态序列化）
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextContent), "text")]
[JsonDerivedType(typeof(ImageContent), "image")]
[JsonDerivedType(typeof(ToolCallContent), "tool_call")]
[JsonDerivedType(typeof(ToolResultContent), "tool_result")]
[JsonDerivedType(typeof(ThinkingContent), "thinking")]
public abstract record ContentItem;

/// <summary>
/// 文本内容
/// </summary>
public record TextContent : ContentItem
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

/// <summary>
/// 图片内容（base64 或 URL）
/// </summary>
public record ImageContent : ContentItem
{
    [JsonPropertyName("media_type")]
    public required string MediaType { get; init; } // image/png, image/jpeg

    [JsonPropertyName("data")]
    public required string Data { get; init; } // base64 or URL
}

/// <summary>
/// 工具调用内容
/// </summary>
public record ToolCallContent : ContentItem
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("arguments")]
    public string Arguments { get; init; } = "{}";
}

/// <summary>
/// 工具结果内容
/// </summary>
public record ToolResultContent : ContentItem
{
    [JsonPropertyName("tool_call_id")]
    public required string ToolCallId { get; init; }

    [JsonPropertyName("tool_name")]
    public required string ToolName { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("is_error")]
    public bool IsError { get; init; }
}

/// <summary>
/// 思考过程内容
/// </summary>
public record ThinkingContent : ContentItem
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}
