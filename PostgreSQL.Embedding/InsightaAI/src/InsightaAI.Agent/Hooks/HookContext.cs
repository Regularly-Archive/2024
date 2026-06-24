using InsightaAI.LLM.Abstractions;

namespace InsightaAI.Agent.Hooks;

/// <summary>
/// AgentHook 执行上下文，承载 LLM 通道和会话级元数据。
/// </summary>
public sealed record HookContext
{
    /// <summary>LLM 客户端（可选，测试场景可为 null）</summary>
    public ILlmClient? LlmClient { get; init; }

    /// <summary>当前会话 ID</summary>
    public required string SessionId { get; init; }
}
