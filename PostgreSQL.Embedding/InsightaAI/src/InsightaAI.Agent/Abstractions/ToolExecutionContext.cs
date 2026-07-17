using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Abstractions;

/// <summary>
/// 工具执行上下文
/// </summary>
public sealed record ToolExecutionContext
{
    /// <summary>Agent ID</summary>
    public required string AgentId { get; init; }

    /// <summary>工具调用 ID</summary>
    public required string ToolCallId { get; init; }

    /// <summary>会话 ID</summary>
    public string? SessionId { get; init; }

    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>服务提供者（可选，Tool/ToolHook 可按需解析服务）</summary>
    public IServiceProvider? Services { get; init; }
}

/// <summary>
/// 工具执行结果
/// </summary>
public sealed record ToolResult
{
    /// <summary>JSON 序列化选项</summary>
    private static readonly System.Text.Json.JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>结果内容</summary>
    public required ContentBlock[] Content { get; init; }

    /// <summary>是否为错误</summary>
    public bool IsError { get; init; }

    /// <summary>工具执行层产出的元数据（遥测层消费，如 MCP server name/description/transport）</summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }

    /// <summary>
    /// 从纯文本创建成功结果
    /// </summary>
    public static ToolResult FromText(string text) => new()
    {
        Content = [new TextBlock { Text = text }]
    };

    /// <summary>
    /// 从对象创建成功结果（序列化为 JSON）
    /// </summary>
    public static ToolResult From<T>(T obj) => new()
    {
        Content = [new TextBlock { Text = System.Text.Json.JsonSerializer.Serialize(obj, DefaultJsonOptions) }]
    };

    /// <summary>
    /// 创建错误结果
    /// </summary>
    public static ToolResult FromError(string error) => new()
    {
        Content = [new TextBlock { Text = error }],
        IsError = true
    };
}
