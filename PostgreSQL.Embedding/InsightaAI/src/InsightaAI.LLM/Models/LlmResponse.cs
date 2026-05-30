namespace InsightaAI.LLM.Models;

/// <summary>
/// LLM 响应
/// </summary>
public sealed record LlmResponse
{
    /// <summary>响应 ID</summary>
    public string? Id { get; init; }

    /// <summary>模型名称</summary>
    public required string Model { get; init; }

    /// <summary>内容块</summary>
    public required ContentBlock[] Content { get; init; }

    /// <summary>完成原因</summary>
    public required DoneReason FinishReason { get; init; }

    /// <summary>Token 用量</summary>
    public TokenUsage? Usage { get; init; }

    /// <summary>原始响应数据 (用于调试)</summary>
    public object? RawResponse { get; init; }

    /// <summary>
    /// 转换为 Message
    /// </summary>
    public Message ToMessage() => new()
    {
        Role = MessageRole.Assistant,
        Content = Content
    };

    /// <summary>
    /// 获取纯文本内容
    /// </summary>
    public string GetTextContent()
    {
        var texts = Content
            .OfType<TextBlock>()
            .Select(b => b.Text);
        return string.Join("", texts);
    }

    /// <summary>
    /// 获取工具调用
    /// </summary>
    public ToolCallBlock[] GetToolCalls()
    {
        return Content
            .OfType<ToolCallBlock>()
            .ToArray();
    }

    /// <summary>
    /// 获取思考内容
    /// </summary>
    public string? GetThinkingContent()
    {
        var thinking = Content
            .OfType<ThinkingBlock>()
            .Select(b => b.Thinking);
        return string.Join("", thinking);
    }
}
