namespace InsightaAI.Agent.Hooks;

/// <summary>
/// AgentHook 执行上下文，承载会话级元数据和服务提供者。
/// </summary>
public sealed record HookContext
{
    /// <summary>当前会话 ID</summary>
    public required string SessionId { get; init; }

    /// <summary>服务提供者（可选，Hook 可按需解析服务）</summary>
    public IServiceProvider? Services { get; init; }
}
