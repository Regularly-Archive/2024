using System.Text.Json;
using System.Text.Json.Serialization;

namespace InsightaAI.LLM.Anthropic;

/// <summary>
/// Anthropic 请求模型
/// </summary>
internal class AnthropicRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("messages")]
    public required AnthropicMessage[] Messages { get; set; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("system")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? System { get; set; }

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; }

    [JsonPropertyName("top_k")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TopK { get; set; }

    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TopP { get; set; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicTool[]? Tools { get; set; }

    [JsonPropertyName("stop_sequences")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? StopSequences { get; set; }

    [JsonPropertyName("thinking")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicThinkingConfig? Thinking { get; set; }
}

/// <summary>
/// Anthropic 消息
/// </summary>
internal class AnthropicMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("content")]
    public required object Content { get; set; }
}

/// <summary>
/// Anthropic 文本内容
/// </summary>
internal class AnthropicTextContent
{
    [JsonPropertyName("type")]
    public string Type => "text";

    [JsonPropertyName("text")]
    public required string Text { get; set; }
}

/// <summary>
/// Anthropic 图片内容
/// </summary>
internal class AnthropicImageContent
{
    [JsonPropertyName("type")]
    public string Type => "image";

    [JsonPropertyName("source")]
    public required AnthropicImageSource Source { get; set; }
}

/// <summary>
/// Anthropic 图片源
/// </summary>
internal class AnthropicImageSource
{
    [JsonPropertyName("type")]
    public string Type => "base64";

    [JsonPropertyName("media_type")]
    public required string MediaType { get; set; }

    [JsonPropertyName("data")]
    public required string Data { get; set; }
}

/// <summary>
/// Anthropic 工具调用内容 (助手消息中)
/// </summary>
internal class AnthropicToolUseContent
{
    [JsonPropertyName("type")]
    public string Type => "tool_use";

    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("input")]
    public required JsonElement Input { get; set; }
}

/// <summary>
/// Anthropic 工具结果内容 (用户消息中)
/// </summary>
internal class AnthropicToolResultContent
{
    [JsonPropertyName("type")]
    public string Type => "tool_result";

    [JsonPropertyName("tool_use_id")]
    public required string ToolUseId { get; set; }

    [JsonPropertyName("content")]
    public required string Content { get; set; }

    [JsonPropertyName("is_error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsError { get; set; }
}

/// <summary>
/// Anthropic 思考内容
/// </summary>
internal class AnthropicThinkingContent
{
    [JsonPropertyName("type")]
    public string Type => "thinking";

    [JsonPropertyName("thinking")]
    public required string Thinking { get; set; }
}

/// <summary>
/// Anthropic 工具定义
/// </summary>
internal class AnthropicTool
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("input_schema")]
    public required JsonElement InputSchema { get; set; }
}

/// <summary>
/// Anthropic Thinking 配置
/// </summary>
internal class AnthropicThinkingConfig
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("budget_tokens")]
    public int BudgetTokens { get; set; }
}
