namespace InsightaAI.LLM.Models;

/// <summary>
/// 对话消息
/// </summary>
public sealed record Message
{
    /// <summary>消息角色</summary>
    public required MessageRole Role { get; init; }

    /// <summary>消息内容 (单个文本或多个内容块)</summary>
    public required ContentBlock[] Content { get; init; }

    /// <summary>工具调用 ID (仅 ToolResult 角色使用)</summary>
    public string? ToolCallId { get; init; }

    /// <summary>工具名称 (仅 ToolResult 角色使用)</summary>
    public string? ToolName { get; init; }

    /// <summary>时间戳</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 从纯文本创建用户消息
    /// </summary>
    public static Message FromUser(string text) => new()
    {
        Role = MessageRole.User,
        Content = [new TextBlock { Text = text }]
    };

    /// <summary>
    /// 从纯文本创建系统消息
    /// </summary>
    public static Message FromSystem(string text) => new()
    {
        Role = MessageRole.System,
        Content = [new TextBlock { Text = text }]
    };

    /// <summary>
    /// 从纯文本创建助手消息
    /// </summary>
    public static Message FromAssistant(string text) => new()
    {
        Role = MessageRole.Assistant,
        Content = [new TextBlock { Text = text }]
    };

    /// <summary>
    /// 创建工具结果消息
    /// </summary>
    public static Message FromToolResult(string toolCallId, string toolName, ContentBlock[] content, bool isError = false) => new()
    {
        Role = MessageRole.ToolResult,
        ToolCallId = toolCallId,
        ToolName = toolName,
        Content = content
    };

    /// <summary>
    /// 获取消息中的纯文本内容
    /// </summary>
    public string GetTextContent()
    {
        var texts = Content
            .OfType<TextBlock>()
            .Select(b => b.Text);
        return string.Join("", texts);
    }

    /// <summary>
    /// 获取消息中的工具调用
    /// </summary>
    public ToolCallBlock[] GetToolCalls()
    {
        return Content
            .OfType<ToolCallBlock>()
            .ToArray();
    }

    /// <summary>
    /// 检查是否包含工具调用
    /// </summary>
    public bool HasToolCalls => Content.Any(c => c is ToolCallBlock);
}
