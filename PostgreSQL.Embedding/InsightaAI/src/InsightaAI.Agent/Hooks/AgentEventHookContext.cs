using InsightaAI.Agent.Models;

namespace InsightaAI.Agent.Hooks;

/// <summary>
/// AgentHook 执行上下文，承载会话级元数据和服务提供者。
/// </summary>
public sealed record AgentEventHookContext
{
    /// <summary>当前会话 ID</summary>
    public required string SessionId { get; init; }

    /// <summary>服务提供者（可选，Hook 可按需解析服务）</summary>
    public IServiceProvider? Services { get; init; }

    public AgentEvent Event { get; private set; }

    public void AttachEvent<TEvent>(TEvent @event) where TEvent : AgentEvent
    {
        Event = @event;
    }
}
