using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Mcp;
using InsightaAI.Agent.Memory;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Skills;
using InsightaAI.Agent.Storage;
using InsightaAI.LLM.Abstractions;

namespace InsightaAI.Agent;

/// <summary>
/// Agent 纯构建器。
///
/// 只负责收集显式配置和依赖，不创建 ServiceProvider，也不负责应用级生命周期。
/// 应用层的 Host、Scope 和服务解析应由调用方负责。
/// </summary>
public sealed class AgentBuilder
{
    private readonly AgentConfig _config;
    private ILlmClient? _llmClient;
    private ToolRegistry _toolRegistry = new();
    private ISkillRegistry? _skillRegistry;
    private McpRegistry? _mcpRegistry;
    private IContextManager? _contextManager;
    private IMemoryManager? _memoryManager;
    private IMessageStorage? _messageStorage;

    public AgentBuilder(AgentConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
    }

    /// <summary>
    /// 设置 LLM 客户端（必需）
    /// </summary>
    public AgentBuilder WithLlm(ILlmClient llmClient)
    {
        ArgumentNullException.ThrowIfNull(llmClient);
        _llmClient = llmClient;
        return this;
    }

    /// <summary>
    /// 设置 ToolRegistry。未设置时使用空注册表。
    /// </summary>
    public AgentBuilder WithToolRegistry(ToolRegistry toolRegistry)
    {
        ArgumentNullException.ThrowIfNull(toolRegistry);
        _toolRegistry = toolRegistry;
        return this;
    }

    public AgentBuilder WithSkillRegistry(ISkillRegistry skillRegistry)
    {
        ArgumentNullException.ThrowIfNull(skillRegistry);
        _skillRegistry = skillRegistry;
        return this;
    }

    public AgentBuilder WithMcpRegistry(McpRegistry mcpRegistry)
    {
        ArgumentNullException.ThrowIfNull(mcpRegistry);
        _mcpRegistry = mcpRegistry;
        return this;
    }

    public AgentBuilder WithContextManager(IContextManager contextManager)
    {
        ArgumentNullException.ThrowIfNull(contextManager);
        _contextManager = contextManager;
        return this;
    }

    public AgentBuilder WithMemoryManager(IMemoryManager memoryManager)
    {
        ArgumentNullException.ThrowIfNull(memoryManager);
        _memoryManager = memoryManager;
        return this;
    }

    public AgentBuilder WithMessageStore(IMessageStorage messageStorage)
    {
        ArgumentNullException.ThrowIfNull(messageStorage);
        _messageStorage = messageStorage;
        return this;
    }

    /// <summary>
    /// 构建 Agent 实例。
    /// </summary>
    public Agent Build()
    {
        if (_llmClient == null)
            throw new InvalidOperationException("LLM 客户端未设置，请先调用 WithLlm()");

        return new Agent(
            _config,
            _llmClient,
            _toolRegistry,
            _skillRegistry,
            _mcpRegistry,
            _contextManager,
            _memoryManager,
            _messageStorage);
    }
}
