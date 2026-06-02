using InsightaAI.Agent.Models;
using InsightaAI.Agent.Tests.Fixtures;
using InsightaAI.Agent.Tools;
using InsightaAI.LLM.Abstractions;
using InsightaAI.Tests.Shared;

namespace InsightaAI.Agent.Tests;

/// <summary>
/// Agent 集成测试 - 使用真实 LLM API
/// </summary>
public class AgentIntegrationTests : AgentTestBase
{
    public AgentIntegrationTests(AgentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Agent_Should_Work_With_OpenAI()
    {
        if (!Fixture.Config.HasOpenAI || Fixture.Config.SkipRealApiCalls)
        {
            return;
        }

        // Arrange
        var agent = Fixture.CreateOpenAIAgent();
        Assert.NotNull(agent);

        // Act
        Console.WriteLine("=== Agent with OpenAI ===");
        Console.WriteLine($"Model: {Fixture.Config.OpenAIModel}");
        Console.WriteLine();

        await PrintStreamEventsAsync(agent, "What time is it now?");
    }

    [Fact]
    public async Task Agent_Should_Work_With_Anthropic()
    {
        if (!Fixture.Config.HasAnthropic || Fixture.Config.SkipRealApiCalls)
        {
            return;
        }

        // Arrange
        var agent = Fixture.CreateAnthropicAgent();
        Assert.NotNull(agent);

        // Act
        Console.WriteLine("=== Agent with Anthropic ===");
        Console.WriteLine($"Model: {Fixture.Config.AnthropicModel}");
        Console.WriteLine();

        await PrintStreamEventsAsync(agent, "What time is it now?");
    }

    [Fact]
    public async Task Agent_Should_Handle_Multiple_Tools()
    {
        if (!Fixture.Config.HasOpenAI || Fixture.Config.SkipRealApiCalls)
        {
            return;
        }

        // Arrange
        var agent = Fixture.CreateOpenAIAgent(
            systemPrompt: @"You are a helpful assistant.
You have access to tools. When you have gathered enough information, use the terminate tool to provide your final answer.");
        Assert.NotNull(agent);

        // Act
        Console.WriteLine("=== Agent with Multiple Tools ===");
        Console.WriteLine();

        var events = await RunAgentAndCollectEventsAsync(agent, "What is the current date and time?");

        // Assert
        AssertAgentCompleted(events);
    }

    [Fact]
    public async Task Agent_Should_Execute_Single_Tool_With_Parameters()
    {
        if (!Fixture.Config.HasOpenAI || Fixture.Config.SkipRealApiCalls)
        {
            return;
        }

        // Arrange - 测试单个工具调用是否能正确传递参数
        var toolRegistry = new ToolRegistry();
        toolRegistry.Register(new SaveNoteTool());

        var agent = Fixture.CreateAgentWithTools("openai", toolRegistry,
            systemPrompt: "You are a helpful assistant. Use the save_note tool to save notes.");
        Assert.NotNull(agent);

        // Act
        Console.WriteLine("=== Single Tool Call Test ===");
        Console.WriteLine($"Model: {Fixture.Config.OpenAIModel}");
        Console.WriteLine();

        await PrintStreamEventsAsync(agent, "Save a note with key='test' and content='Hello World'");
    }

    [Fact]
    public async Task Agent_Should_Execute_Parallel_Tool_Calls()
    {
        if (!Fixture.Config.HasOpenAI || Fixture.Config.SkipRealApiCalls)
        {
            return;
        }

        // Arrange - 注册多个工具，让 LLM 可能同时调用
        var toolRegistry = new ToolRegistry();
        toolRegistry.Register(new GetCurrentTimeTool());
        toolRegistry.Register(new SaveNoteTool());

        var agent = Fixture.CreateAgentWithTools("openai", toolRegistry,
            systemPrompt: @"You are a helpful assistant.
You have access to tools. When the user asks for multiple things, call ALL relevant tools at once in the same response.
For example, if asked to get time and save a note, call both tools simultaneously.",
            maxToolRounds: 5);
        Assert.NotNull(agent);

        // Act
        Console.WriteLine("=== Parallel Tool Calls Test ===");
        Console.WriteLine($"Model: {Fixture.Config.OpenAIModel}");
        Console.WriteLine();

        await PrintStreamEventsAsync(agent, "What time is it? Also save a note: key='meeting', content='Meeting at 3pm'");
    }

    [Fact]
    public async Task Agent_Should_Complete_Simple_Task_Without_Tools()
    {
        if (!Fixture.Config.HasOpenAI || Fixture.Config.SkipRealApiCalls)
        {
            return;
        }

        // Arrange
        var emptyToolRegistry = new ToolRegistry();
        var agent = Fixture.CreateAgentWithTools("openai", emptyToolRegistry,
            systemPrompt: "You are a helpful assistant.",
            maxToolRounds: 1);
        Assert.NotNull(agent);

        // Act
        Console.WriteLine("=== Simple Task (No Tools) ===");
        Console.WriteLine();

        var result = await RunAgentAndGetResultAsync(agent, "Say 'Hello, World!' and nothing else.");

        Console.WriteLine($"Response: {result.Message.GetTextContent()}");

        // Assert
        Assert.Equal(AgentStatus.Completed, result.Status);
        Assert.Equal(1, result.Rounds);
        Assert.Contains("Hello", result.Message.GetTextContent(), StringComparison.OrdinalIgnoreCase);
    }
}
