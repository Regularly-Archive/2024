using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Abstractions;

namespace InsightaAI.Agent.Diagnostics;

/// <summary>
/// 便捷扩展方法 — 用于快速启用 Agent telemetry
/// </summary>
public static class AgentTelemetryExtensions
{
    /// <summary>
    /// 用 telemetry 装饰 ILlmClient
    /// </summary>
    public static ILlmClient WithTelemetry(this ILlmClient client)
        => new TelemetryLlmClient(client);

    /// <summary>
    /// 用 telemetry 包装 ToolCallHandler 委托
    /// </summary>
    public static Tools.ToolCallHandler WithTelemetry(this Tools.ToolCallHandler handler)
        => TelemetryToolCallHandler.Wrap(handler);

    /// <summary>
    /// 为 Agent 添加完整的 OpenTelemetry 插桩（LLM + Tool + Session/Round）
    /// </summary>
    /// <param name="agent">Agent 实例</param>
    /// <param name="sessionId">会话 ID（可选，用于 session span 标记）</param>
    /// <returns>TelemetryHook 实例（可用于后续 SetSessionContext 更新）</returns>
    public static AgentTelemetryHook AddTelemetry(this Agent agent, string? sessionId = null)
    {
        var agentId = agent.Config.Id;

        // 设置 LLM client 包装器（构造时持有 agentId，运行时从字典查找 round Activity）
        agent.LlmClientDecorator = client => new TelemetryLlmClient(client, agentId);

        // 设置 tool handler 包装器（同上）
        agent.ToolCallHandlerDecorator = handler => TelemetryToolCallHandler.Wrap(handler, agentId);

        // 添加 session/round hook
        var hook = new AgentTelemetryHook();
        hook.SetSessionContext(
            agent.Config.Id,
            agent.Config.Name,
            agent.Config.Model,
            sessionId ?? "");
        agent.AddAgentHook(hook);

        return hook;
    }

    /// <summary>
    /// 创建带完整 telemetry 的 Agent（手动依赖注入版本）
    /// </summary>
    /// <remarks>
    /// 推荐使用 <see cref="AddTelemetry"/> 替代——先正常构造 Agent，再调用 agent.AddTelemetry()。
    /// </remarks>
    [Obsolete("优先使用 AddTelemetry() 扩展方法。此工厂方法参数过多且与 Agent 构造函数重复。")]
    public static Agent CreateInstrumented(
        Models.AgentConfig config,
        ILlmClient llmClient,
        ToolRegistry toolRegistry,
        Skills.ISkillRegistry? skillRegistry = null,
        Mcp.McpRegistry? mcpRegistry = null,
        Context.IContextManager? contextManager = null,
        Memory.IMemoryManager? memoryManager = null,
        Storage.IMessageStorage? messageStorage = null,
        string? sessionId = null)
    {
        var instrumentedLlm = new TelemetryLlmClient(llmClient);

        var agent = new Agent(
            config, instrumentedLlm, toolRegistry,
            skillRegistry, mcpRegistry, contextManager,
            memoryManager, messageStorage);

        agent.AddTelemetry(sessionId);

        return agent;
    }
}
