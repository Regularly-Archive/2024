using System.Text.Json.Serialization;

namespace InsightaAI.Agent.Cli.Models;

/// <summary>
/// 会话消息（JSONL 格式）
/// </summary>
public class SessionMessage
{
    /// <summary>
    /// 消息 ID
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// 角色：user, assistant, tool, system
    /// </summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    /// <summary>
    /// 消息内容
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    /// <summary>
    /// 工具调用（如果有）
    /// </summary>
    [JsonPropertyName("tool_calls")]
    public List<ToolCallInfo>? ToolCalls { get; set; }

    /// <summary>
    /// 工具结果（如果有）
    /// </summary>
    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }

    /// <summary>
    /// 工具名称（用于工具结果消息）
    /// </summary>
    [JsonPropertyName("tool_name")]
    public string? ToolName { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 元数据
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// 工具调用信息
/// </summary>
public class ToolCallInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "{}";
}

/// <summary>
/// 会话信息
/// </summary>
public class SessionInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "";

    [JsonPropertyName("message_count")]
    public int MessageCount { get; set; }
}
