namespace InsightaAI.LLM.Models;

/// <summary>
/// 流式事件基类
/// </summary>
public abstract record StreamEvent
{
    public abstract string Type { get; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 流开始事件
/// </summary>
public sealed record StreamStartEvent : StreamEvent
{
    public override string Type => "start";
    public required string Model { get; init; }
    public required string Provider { get; init; }
}

/// <summary>
/// 文本生成开始事件
/// </summary>
public sealed record TextStartEvent : StreamEvent
{
    public override string Type => "text_start";
    public int ContentIndex { get; init; }
}

/// <summary>
/// 文本增量事件
/// </summary>
public sealed record TextDeltaEvent : StreamEvent
{
    public override string Type => "text_delta";
    public required string Delta { get; init; }
    public int ContentIndex { get; init; }
}

/// <summary>
/// 文本生成结束事件
/// </summary>
public sealed record TextEndEvent : StreamEvent
{
    public override string Type => "text_end";
    public int ContentIndex { get; init; }
}

/// <summary>
/// 思考开始事件
/// </summary>
public sealed record ThinkingStartEvent : StreamEvent
{
    public override string Type => "thinking_start";
    public int ContentIndex { get; init; }
}

/// <summary>
/// 思考增量事件
/// </summary>
public sealed record ThinkingDeltaEvent : StreamEvent
{
    public override string Type => "thinking_delta";
    public required string Delta { get; init; }
    public int ContentIndex { get; init; }
}

/// <summary>
/// 思考结束事件
/// </summary>
public sealed record ThinkingEndEvent : StreamEvent
{
    public override string Type => "thinking_end";
    public int ContentIndex { get; init; }
}

/// <summary>
/// 工具调用开始事件
/// </summary>
public sealed record ToolCallStartEvent : StreamEvent
{
    public override string Type => "toolcall_start";
    public int ContentIndex { get; init; }
    public required string ToolName { get; init; }
    public string? ToolCallId { get; init; }
}

/// <summary>
/// 工具调用参数增量事件
/// </summary>
public sealed record ToolCallDeltaEvent : StreamEvent
{
    public override string Type => "toolcall_delta";
    public int ContentIndex { get; init; }
    public required string ArgumentsDelta { get; init; }
    public ToolCallBlock? Partial { get; init; }
}

/// <summary>
/// 工具调用结束事件
/// </summary>
public sealed record ToolCallEndEvent : StreamEvent
{
    public override string Type => "toolcall_end";
    public required ToolCallBlock ToolCall { get; init; }
}

/// <summary>
/// 流完成事件（包含 Token 用量）
/// </summary>
public sealed record DoneEvent : StreamEvent
{
    public override string Type => "done";
    public required DoneReason Reason { get; init; }
    public TokenUsage? Usage { get; init; }
    public Message? Message { get; init; }
}

/// <summary>
/// 错误事件
/// </summary>
public sealed record ErrorEvent : StreamEvent
{
    public override string Type => "error";
    public required Exception Error { get; init; }
    public bool Recoverable { get; init; }
}

/// <summary>
/// 流完成原因
/// </summary>
public enum DoneReason
{
    /// <summary>正常完成</summary>
    Complete,

    /// <summary>需要执行工具调用</summary>
    ToolCalls,

    /// <summary>被停止序列中断</summary>
    Stop,

    /// <summary>达到最大 token 限制</summary>
    MaxTokens,

    /// <summary>被用户中止</summary>
    Aborted,

    /// <summary>发生错误</summary>
    Error
}
