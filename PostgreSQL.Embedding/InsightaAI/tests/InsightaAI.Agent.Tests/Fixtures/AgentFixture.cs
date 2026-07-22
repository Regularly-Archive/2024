using InsightaAI.Agent.Models;
using InsightaAI.Agent.Tools;
using InsightaAI.LLM;
using InsightaAI.LLM.Abstractions;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Anthropic;
using InsightaAI.LLM.Models;
using InsightaAI.LLM.OpenAI;
using InsightaAI.Tests.Shared;

namespace InsightaAI.Agent.Tests.Fixtures;

/// <summary>
/// Agent 集成测试 Fixture - 共享重量级资源
/// </summary>
public class AgentFixture : IDisposable
{
    public TestConfig Config { get; }
    public LlmClientFactory Factory { get; }
    public ToolRegistry SharedTools { get; }

    public AgentFixture()
    {
        Config = new TestConfig();
        Factory = new LlmClientFactory();
        Factory.RegisterAdapter(new OpenAIAdapter());
        Factory.RegisterAdapter(new AnthropicAdapter());

        SharedTools = new ToolRegistry();
        SharedTools.Register(new GetCurrentTimeTool());
        SharedTools.Register(new TerminateTool());
    }

    /// <summary>
    /// 创建 OpenAI Agent
    /// </summary>
    public Agent? CreateOpenAIAgent(string? systemPrompt = null, int maxToolRounds = 5)
    {
        if (!Config.HasOpenAI) return null;

        var client = Factory.Create("openai", Config.GetOpenAIConfig()!);
        var agentConfig = new AgentConfig
        {
            Id = "openai-agent",
            Name = "OpenAI Agent",
            CustomInstructions = systemPrompt ?? "You are a helpful assistant. Use tools when appropriate.",
            Model = Config.OpenAIModel,
            MaxToolRounds = maxToolRounds
        };

        return new Agent(agentConfig, client, SharedTools);
    }

    /// <summary>
    /// 创建 Anthropic Agent
    /// </summary>
    public Agent? CreateAnthropicAgent(string? systemPrompt = null, int maxToolRounds = 5)
    {
        if (!Config.HasAnthropic) return null;

        var client = Factory.Create("anthropic", Config.GetAnthropicConfig()!);
        var agentConfig = new AgentConfig
        {
            Id = "anthropic-agent",
            Name = "Anthropic Agent",
            CustomInstructions = systemPrompt ?? "You are a helpful assistant. Use tools when appropriate.",
            Model = Config.AnthropicModel,
            MaxToolRounds = maxToolRounds
        };

        return new Agent(agentConfig, client, SharedTools);
    }

    /// <summary>
    /// 创建带自定义工具的 Agent
    /// </summary>
    public Agent? CreateAgentWithTools(
        string provider,
        ToolRegistry toolRegistry,
        string? systemPrompt = null,
        int maxToolRounds = 5)
    {
        ILlmClient client;
        string model;

        if (provider == "openai" && Config.HasOpenAI)
        {
            client = Factory.Create("openai", Config.GetOpenAIConfig()!);
            model = Config.OpenAIModel;
        }
        else if (provider == "anthropic" && Config.HasAnthropic)
        {
            client = Factory.Create("anthropic", Config.GetAnthropicConfig()!);
            model = Config.AnthropicModel;
        }
        else
        {
            return null;
        }

        var agentConfig = new AgentConfig
        {
            Id = $"{provider}-agent",
            Name = $"{provider} Agent",
            CustomInstructions = systemPrompt ?? "You are a helpful assistant.",
            Model = model,
            MaxToolRounds = maxToolRounds
        };

        return new Agent(agentConfig, client, toolRegistry);
    }

    public void Dispose()
    {
        // 清理资源（如果有）
    }
}
