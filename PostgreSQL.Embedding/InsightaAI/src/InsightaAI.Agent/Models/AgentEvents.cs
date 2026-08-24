using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Models;

/// <summary>
/// Agent 事件类型
/// </summary>
public enum AgentEventType
{
    /// <summary>Agent 已接收用户输入</summary>
    UserPrompt,

    /// <summary>Agent 开始处理一次用户输入</summary>
    TurnStart,

    /// <summary>新一轮 LLM 调用开始</summary>
    RoundStart,

    /// <summary>LLM 流式事件透传</summary>
    LlmStream,

    /// <summary>工具开始执行</summary>
    ToolStart,

    /// <summary>Execution-time progress reported by a tool.</summary>
    ToolProgress,

    /// <summary>工具执行完成</summary>
    ToolEnd,

    /// <summary>一轮结束</summary>
    RoundEnd,

    /// <summary>Agent 完成一次用户输入的处理</summary>
    TurnEnd,

    /// <summary>错误</summary>
    Error,

    /// <summary>上下文已压缩</summary>
    ContextCompacted
}

/// <summary>
/// Agent 事件基类
/// </summary>
public abstract record AgentEvent
{
    public abstract AgentEventType Type { get; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required string AgentId { get; init; }
}

/// <summary>
/// Agent 已接收用户输入。该事件用于 Hook 上下文，不会通过 Agent 的公开事件流转发。
/// 用户消息内容作为 Hook 的瞬时参数传递，避免进入通用事件载荷。
/// </summary>
public sealed record AgentUserPromptEvent : AgentEvent
{
    public override AgentEventType Type => AgentEventType.UserPrompt;
    public required string Input { get; init; }
}

/// <summary>
/// Agent Turn 开始事件
/// </summary>
public sealed record AgentTurnStartEvent : AgentEvent
{
    public override AgentEventType Type => AgentEventType.TurnStart;
    public required string AgentName { get; init; }
    public required string Model { get; init; }
}

/// <summary>
/// 轮次开始事件
/// </summary>
public sealed record AgentRoundStartEvent : AgentEvent
{
    public override AgentEventType Type => AgentEventType.RoundStart;
    public int Round { get; init; }
}

/// <summary>
/// LLM 流式事件透传
/// </summary>
public sealed record AgentLlmStreamEvent : AgentEvent
{
    public override AgentEventType Type => AgentEventType.LlmStream;
    public required StreamEvent StreamEvent { get; init; }
}

/// <summary>
/// 工具开始执行事件
/// </summary>
public sealed record AgentToolStartEvent : AgentEvent
{
    public override AgentEventType Type => AgentEventType.ToolStart;
    public required string ToolCallId { get; init; }
    public required string ToolName { get; init; }
    public required string Arguments { get; init; }
}

/// <summary>
/// Tool execution progress visible to UI observers only. It is never persisted to conversation
/// history or written to the Agent event log.
/// </summary>
public sealed record AgentToolProgressEvent : AgentEvent
{
    public override AgentEventType Type => AgentEventType.ToolProgress;
    public required string ToolCallId { get; init; }
    public required string ToolName { get; init; }
    public required ToolProgressUpdate Progress { get; init; }
}

/// <summary>
/// 工具执行完成事件
/// </summary>
public sealed record AgentToolEndEvent : AgentEvent
{
    public override AgentEventType Type => AgentEventType.ToolEnd;
    public required string ToolCallId { get; init; }
    public required string ToolName { get; init; }
    public bool IsError { get; init; }
    public string? ResultPreview { get; init; }
}

/// <summary>
/// 轮次结束事件
/// </summary>
public sealed record AgentRoundEndEvent : AgentEvent
{
    public override AgentEventType Type => AgentEventType.RoundEnd;
    public int Round { get; init; }
    public bool HasToolCalls { get; init; }
}

/// <summary>
/// Agent Turn 结束事件
/// </summary>
public sealed record AgentTurnEndEvent : AgentEvent
{
    public override AgentEventType Type => AgentEventType.TurnEnd;
    public required AgentResult Result { get; init; }
}

/// <summary>
/// Agent 错误事件
/// </summary>
public sealed record AgentErrorEvent : AgentEvent
{
    public override AgentEventType Type => AgentEventType.Error;
    public required string ErrorMessage { get; init; }
    public bool Recoverable { get; init; }
}

/// <summary>
/// 上下文压缩事件
/// </summary>
public sealed record AgentContextCompactedEvent : AgentEvent
{
    public override AgentEventType Type => AgentEventType.ContextCompacted;
    public required string Strategy { get; init; }
    public int PreCompactTokens { get; init; }
    public int PostCompactTokens { get; init; }
    public int PreCompactMessages { get; init; }
    public int PostCompactMessages { get; init; }

    /// <summary>压缩后的消息列表（用于同步到 Session）</summary>
    public Message[]? CompactedMessages { get; init; }
}
