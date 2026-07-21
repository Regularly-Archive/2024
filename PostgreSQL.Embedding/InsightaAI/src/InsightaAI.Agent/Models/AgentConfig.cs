using InsightaAI.LLM;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Models;

/// <summary>
/// Agent 执行状态
/// </summary>
public enum AgentStatus
{
    /// <summary>空闲</summary>
    Idle,

    /// <summary>运行中</summary>
    Running,

    /// <summary>等待工具执行</summary>
    WaitingTool,

    /// <summary>已完成</summary>
    Completed,

    /// <summary>失败</summary>
    Failed,

    /// <summary>已中止</summary>
    Aborted
}

/// <summary>
/// Agent 配置
/// </summary>
public sealed record AgentConfig
{
    /// <summary>Agent 唯一标识</summary>
    public required string Id { get; init; }

    /// <summary>Agent 名称</summary>
    public required string Name { get; init; }

    /// <summary>系统提示词</summary>
    public required string SystemPrompt { get; init; }

    /// <summary>使用的模型</summary>
    public required string Model { get; init; }

    /// <summary>温度</summary>
    public double? Temperature { get; init; }

    /// <summary>最大 token 数</summary>
    public int? MaxTokens { get; init; }

    /// <summary>最大工具调用轮次 (防止无限循环)，默认 15</summary>
    public int MaxToolRounds { get; init; } = 15;

    /// <summary>是否并行执行同一轮中的多个工具调用，默认 true</summary>
    public bool ParallelToolExecution { get; init; } = true;

    /// <summary>用户 ID（用于记忆系统）</summary>
    public string? UserId { get; init; }

    /// <summary>工作目录（用于加载 AGENTS.md 等项目上下文）</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>自定义元数据</summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Agent 执行结果
/// </summary>
public sealed record AgentResult
{
    /// <summary>执行状态</summary>
    public required AgentStatus Status { get; init; }

    /// <summary>最终消息</summary>
    public required Message Message { get; init; }

    /// <summary>Token 用量</summary>
    public TokenUsage? Usage { get; init; }

    /// <summary>工具调用轮次</summary>
    public int Rounds { get; init; }

    /// <summary>执行时长 (ms)</summary>
    public long DurationMs { get; init; }

    /// <summary>错误信息 (如果失败)</summary>
    public string? Error { get; init; }

    /// <summary>预估的上下文 token 数</summary>
    public int EstimatedContextTokens { get; init; }

    /// <summary>最大上下文窗口大小</summary>
    public int MaxContextTokens { get; init; }

    /// <summary>扣除输出预留后的可用输入预算</summary>
    public int AvailableInputTokens { get; init; }
}

/// <summary>
/// Agent 上下文
/// </summary>
public sealed record AgentContext
{
    /// <summary>会话 ID</summary>
    public string? SessionId { get; init; }

    /// <summary>对话历史</summary>
    public List<Message> History { get; init; } = [];

    /// <summary>工作状态</summary>
    public Dictionary<string, object> State { get; init; } = [];

    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }
}
