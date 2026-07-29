using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Mcp;
using InsightaAI.Agent.Memory;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Skills;
using InsightaAI.Agent.Storage;
using InsightaAI.LLM.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace InsightaAI.Agent;

/// <summary>
/// Agent 构建器。
///
/// 负责收集 Agent 配置和依赖，并在 Build 时创建只属于当前 Agent 的
/// ServiceProvider。Agent 会在释放时一并释放该 ServiceProvider。
/// </summary>
public sealed class AgentBuilder
{
    private readonly AgentConfig _config;
    private readonly ServiceCollection _services = new();
    private ILlmClient? _llmClient;

    public AgentBuilder(AgentConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;

        _services.AddSingleton(config);
        _services.AddSingleton(new ToolRegistry());
    }

    /// <summary>
    /// 设置 LLM 客户端（必需）
    /// </summary>
    public AgentBuilder WithLlm(ILlmClient llmClient)
    {
        ArgumentNullException.ThrowIfNull(llmClient);
        _llmClient = llmClient;
        _services.RemoveAll<ILlmClient>();
        _services.AddSingleton(llmClient);
        return this;
    }

    /// <summary>
    /// 设置 ToolRegistry。未设置时使用空注册表。
    /// </summary>
    public AgentBuilder WithToolRegistry(ToolRegistry toolRegistry)
    {
        ArgumentNullException.ThrowIfNull(toolRegistry);
        _services.RemoveAll<ToolRegistry>();
        _services.AddSingleton(toolRegistry);
        return this;
    }

    public AgentBuilder WithSkillRegistry(ISkillRegistry skillRegistry)
    {
        ArgumentNullException.ThrowIfNull(skillRegistry);
        _services.RemoveAll<ISkillRegistry>();
        _services.AddSingleton(skillRegistry);
        return this;
    }

    public AgentBuilder WithMcpRegistry(McpRegistry mcpRegistry)
    {
        ArgumentNullException.ThrowIfNull(mcpRegistry);
        _services.RemoveAll<McpRegistry>();
        _services.AddSingleton(mcpRegistry);
        return this;
    }

    public AgentBuilder WithContextManager(IContextManager contextManager)
    {
        ArgumentNullException.ThrowIfNull(contextManager);
        _services.RemoveAll<IContextManager>();
        _services.AddSingleton(contextManager);
        return this;
    }

    public AgentBuilder WithMemoryManager(IMemoryManager memoryManager)
    {
        ArgumentNullException.ThrowIfNull(memoryManager);
        _services.RemoveAll<IMemoryManager>();
        _services.AddSingleton(memoryManager);
        return this;
    }

    public AgentBuilder WithMessageStore(IMessageStorage messageStorage)
    {
        ArgumentNullException.ThrowIfNull(messageStorage);
        _services.RemoveAll<IMessageStorage>();
        _services.AddSingleton(messageStorage);
        return this;
    }

    /// <summary>
    /// Supplies the logging factory used by this Agent's private service provider.
    /// </summary>
    public AgentBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _services.RemoveAll<ILoggerFactory>();
        _services.RemoveAll(typeof(ILogger<>));
        _services.AddSingleton(loggerFactory);
        _services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        return this;
    }

    /// <summary>
    /// 注册当前 Agent 可访问的服务。
    /// </summary>
    public AgentBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_services);
        return this;
    }

    /// <summary>
    /// 构建 Agent 实例。
    /// </summary>
    public Agent Build()
    {
        if (_llmClient == null)
            throw new InvalidOperationException("LLM 客户端未设置，请先调用 WithLlm()");

        var serviceProvider = _services.BuildServiceProvider();
        try
        {
            return new Agent(_config, serviceProvider);
        }
        catch
        {
            serviceProvider.Dispose();
            throw;
        }
    }
}
