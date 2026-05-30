using System.Text.Json;

namespace InsightaAI.LLM.Models;

/// <summary>
/// 工具定义
/// </summary>
public sealed record ToolDefinition
{
    /// <summary>工具名称</summary>
    public required string Name { get; init; }

    /// <summary>工具描述</summary>
    public required string Description { get; init; }

    /// <summary>工具参数 JSON Schema</summary>
    public required JsonElement Schema { get; init; }
}

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
    public string? ConversationId { get; init; }

    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// 工具执行结果
/// </summary>
public sealed record ToolResult
{
    /// <summary>结果内容</summary>
    public required ContentBlock[] Content { get; init; }

    /// <summary>是否为错误</summary>
    public bool IsError { get; init; }

    /// <summary>
    /// 从纯文本创建成功结果
    /// </summary>
    public static ToolResult FromText(string text) => new()
    {
        Content = [new TextBlock { Text = text }]
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
