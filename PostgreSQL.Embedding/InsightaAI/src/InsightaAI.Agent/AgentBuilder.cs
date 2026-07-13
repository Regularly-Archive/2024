using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Mcp;
using InsightaAI.Agent.Memory;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Skills;
using InsightaAI.Agent.Storage;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Anthropic;
using InsightaAI.LLM.Extensions;
using InsightaAI.LLM.Gemini;
using InsightaAI.LLM.OpenAI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InsightaAI.Agent;

/// <summary>
/// Agent 构建器 - 提供 fluent API 组装 Agent，内部维护 ServiceCollection
/// </summary>
public class AgentBuilder
{
    private readonly AgentConfig _config;
    private readonly IServiceCollection _services;

    public AgentBuilder(AgentConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _config = config;
        _services = new ServiceCollection();

        // 注册基础服务
        _services.TryAddSingleton(_config);
        _services.AddLlmClientFactory(factory =>
        {
            factory.RegisterAdapter(new OpenAIAdapter());
            factory.RegisterAdapter(new OpenAIResponseAdapter());
            factory.RegisterAdapter(new AnthropicAdapter());
            factory.RegisterAdapter(new GeminiAdapter());
        });
    }

    /// <summary>
    /// 设置 LLM 客户端（必需）
    /// </summary>
    public AgentBuilder WithLlm(ILlmClient llmClient)
    {
        ArgumentNullException.ThrowIfNull(llmClient);
        _services.TryAddSingleton(llmClient);
        return this;
    }

    /// <summary>
    /// 设置 ToolRegistry
    /// </summary>
    public AgentBuilder WithToolRegistry(ToolRegistry toolRegistry)
    {
        ArgumentNullException.ThrowIfNull(toolRegistry);
        _services.TryAddSingleton(toolRegistry);
        return this;
    }

    /// <summary>
    /// 设置 SkillRegistry
    /// </summary>
    public AgentBuilder WithSkillRegistry(ISkillRegistry skillRegistry)
    {
        ArgumentNullException.ThrowIfNull(skillRegistry);
        _services.TryAddSingleton(skillRegistry);
        return this;
    }

    /// <summary>
    /// 设置 McpRegistry
    /// </summary>
    public AgentBuilder WithMcpRegistry(McpRegistry mcpRegistry)
    {
        ArgumentNullException.ThrowIfNull(mcpRegistry);
        _services.TryAddSingleton(mcpRegistry);
        return this;
    }

    /// <summary>
    /// 设置上下文管理器
    /// </summary>
    public AgentBuilder WithContextManager(IContextManager contextManager)
    {
        ArgumentNullException.ThrowIfNull(contextManager);
        _services.TryAddSingleton(contextManager);
        return this;
    }

    /// <summary>
    /// 设置记忆管理器
    /// </summary>
    public AgentBuilder WithMemoryManager(IMemoryManager memoryManager)
    {
        ArgumentNullException.ThrowIfNull(memoryManager);
        _services.TryAddSingleton(memoryManager);
        return this;
    }

    /// <summary>
    /// 设置消息存储
    /// </summary>
    public AgentBuilder WithMessageStore(IMessageStorage messageStorage)
    {
        ArgumentNullException.ThrowIfNull(messageStorage);
        _services.TryAddSingleton(messageStorage);
        return this;
    }

    /// <summary>
    /// 配置自定义服务
    /// </summary>
    public AgentBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_services);
        return this;
    }

    /// <summary>
    /// 构建 Agent 实例
    /// </summary>
    /// <returns>Agent 实例</returns>
    /// <exception cref="InvalidOperationException">缺少必需组件时抛出</exception>
    public Agent Build()
    {
        // 检查必需的组件
        if (!_services.Any(sd => sd.ServiceType == typeof(ILlmClient)))
            throw new InvalidOperationException("LLM 客户端未设置，请先调用 WithLlm()");

        var serviceProvider = _services.BuildServiceProvider();
        return new Agent(_config, serviceProvider);
    }
}
