using InsightaAI.Agent.Models;

namespace InsightaAI.Agent.Hooks;

/// <summary>
/// AgentHook 执行上下文，承载会话级元数据和服务提供者。
/// </summary>
public sealed record AgentEventHookContext
{
    private AgentEventHookContext(
        string sessionId,
        AgentEvent @event,
        IServiceProvider? services)
    {
        SessionId = sessionId;
        Event = @event;
        Services = services;
    }

    /// <summary>当前会话 ID</summary>
    public string SessionId { get; }

    /// <summary>触发当前 Hook 的不可变事件快照。</summary>
    public AgentEvent Event { get; }

    /// <summary>
    /// 服务提供者（可选，Hook 可按需解析 Agent 级扩展服务）。
    /// 该 Provider 不提供 Scoped 生命周期语义；后台 Hook 不得依赖 Scoped 服务。
    /// </summary>
    public IServiceProvider? Services { get; }

    /// <summary>创建一次 Hook 调用所需的事件上下文快照。</summary>
    public static AgentEventHookContext Create(
        string sessionId,
        AgentEvent @event,
        IServiceProvider? services = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(@event);

        return new AgentEventHookContext(sessionId, @event, services);
    }

    /// <summary>以期望的具体事件类型读取当前事件。</summary>
    public TEvent GetEvent<TEvent>() where TEvent : AgentEvent
    {
        return Event as TEvent ?? throw new InvalidOperationException(
            $"Expected {typeof(TEvent).Name}, but received {Event.GetType().Name}.");
    }
}
