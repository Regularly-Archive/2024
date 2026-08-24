using System.Text.Json;
using System.Text.Json.Serialization;

namespace InsightaAI.LLM.OpenAI;

/// <summary>
/// Responses API 请求模型
/// </summary>
internal class ResponsesRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("input")]
    public required object Input { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("max_output_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxOutputTokens { get; set; }

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResponseTool[]? Tools { get; set; }

    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ToolChoice { get; set; }

    [JsonPropertyName("reasoning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResponseReasoning? Reasoning { get; set; }

    [JsonPropertyName("stop")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Stop { get; set; }
}

/// <summary>
/// Responses API 工具定义
/// </summary>
internal class ResponseTool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Parameters { get; set; }
}

/// <summary>
/// Responses API 推理配置
/// </summary>
internal class ResponseReasoning
{
    [JsonPropertyName("effort")]
    public required string Effort { get; set; }
}

/// <summary>
/// Responses API 消息输入 item（type: "message"）
/// </summary>
internal class ResponseMessageItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "message";

    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("content")]
    public required object[] Content { get; set; }
}

/// <summary>
/// Responses API 输入文本内容
/// </summary>
internal class ResponseInputText
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "input_text";

    [JsonPropertyName("text")]
    public required string Text { get; set; }
}

/// <summary>
/// Responses API 输出文本内容
/// </summary>
internal class ResponseOutputText
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "output_text";

    [JsonPropertyName("text")]
    public required string Text { get; set; }
}

internal class ResponseReasoningItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "reasoning";

    [JsonPropertyName("content")]
    public required object[] Content { get; set; }
}

internal class ResponseReasoningText
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "reasoning_text";

    [JsonPropertyName("text")]
    public required string Text { get; set; }
}

/// <summary>
/// Responses API 函数调用 item（type: "function_call"）
/// </summary>
internal class ResponseFunctionCallItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function_call";

    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("call_id")]
    public required string CallId { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("arguments")]
    public required string Arguments { get; set; }
}

/// <summary>
/// Responses API 函数调用输出 item（type: "function_call_output"）
/// </summary>
internal class ResponseFunctionCallOutputItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function_call_output";

    [JsonPropertyName("call_id")]
    public required string CallId { get; set; }

    [JsonPropertyName("output")]
    public required string Output { get; set; }
}

// ── 非流式响应模型 ──

/// <summary>
/// Responses API 完整响应
/// </summary>
internal class ResponsesResult
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("output")]
    public ResponseOutputItem[]? Output { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("usage")]
    public ResponseUsage? Usage { get; set; }
}

/// <summary>
/// Responses API 输出 item（message 或 function_call）
/// </summary>
internal class ResponseOutputItem
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public ResponseContentPart[]? Content { get; set; }

    [JsonPropertyName("call_id")]
    public string? CallId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

/// <summary>
/// Responses API 内容部分
/// </summary>
internal class ResponseContentPart
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

/// <summary>
/// Responses API Token 用量
/// </summary>
internal class ResponseUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
