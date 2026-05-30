using System.Text.Json;

namespace InsightaAI.LLM.Models;

/// <summary>
/// 内容块基类
/// </summary>
public abstract record ContentBlock
{
    public abstract string Type { get; }
}

/// <summary>
/// 文本块
/// </summary>
public sealed record TextBlock : ContentBlock
{
    public override string Type => "text";
    public required string Text { get; init; }
}

/// <summary>
/// 工具调用块
/// </summary>
public sealed record ToolCallBlock : ContentBlock
{
    public override string Type => "toolCall";
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required JsonElement Arguments { get; init; }
}

/// <summary>
/// 思考过程块 (Claude extended thinking / DeepSeek reasoning)
/// </summary>
public sealed record ThinkingBlock : ContentBlock
{
    public override string Type => "thinking";
    public required string Thinking { get; init; }
}

/// <summary>
/// 图片块
/// </summary>
public sealed record ImageBlock : ContentBlock
{
    public override string Type => "image";
    public required ImageSource Source { get; init; }
}

/// <summary>
/// 图片源
/// </summary>
public sealed record ImageSource
{
    public required string MediaType { get; init; }
    public required string Data { get; init; }
}

/// <summary>
/// 工具结果块
/// </summary>
public sealed record ToolResultBlock : ContentBlock
{
    public override string Type => "toolResult";
    public required string ToolCallId { get; init; }
    public required string ToolName { get; init; }
    public required ContentBlock[] Content { get; init; }
    public bool IsError { get; init; }
}
