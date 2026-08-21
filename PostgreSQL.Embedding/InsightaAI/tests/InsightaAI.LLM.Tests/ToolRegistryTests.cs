using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;
using InsightaAI.LLM.Tests.Tools;
using InsightaAI.Tests.Shared;
using Xunit;

namespace InsightaAI.LLM.Tests;

/// <summary>
/// 工具注册表测试
/// </summary>
public class ToolRegistryTests
{
    private readonly ToolRegistry _registry;

    public ToolRegistryTests()
    {
        _registry = new ToolRegistry();
    }

    [Fact]
    public void Register_Should_Add_Tool()
    {
        var tool = new CalculatorTool();
        _registry.Register(tool);

        Assert.True(_registry.HasTool("calculator"));
        Assert.Contains("calculator", _registry.GetRegisteredToolNames());
    }

    [Fact]
    public void RegisterAll_Should_Add_Multiple_Tools()
    {
        var tools = new List<ITool>
        {
            new CalculatorTool(),
            new MockTool("tool_a"),
            new MockTool("tool_b")
        };

        _registry.RegisterAll(tools);

        Assert.True(_registry.HasTool("calculator"));
        Assert.True(_registry.HasTool("tool_a"));
        Assert.True(_registry.HasTool("tool_b"));
        Assert.Equal(3, _registry.GetRegisteredToolNames().Count());
    }

    [Fact]
    public void GetDefinitions_Should_Return_All_Tool_Definitions()
    {
        _registry.Register(new CalculatorTool());
        _registry.Register(new MockTool("mock"));

        var definitions = _registry.GetDefinitions();

        Assert.Equal(2, definitions.Length);
        Assert.Contains(definitions, d => d.Name == "calculator");
        Assert.Contains(definitions, d => d.Name == "mock");
    }

    [Fact]
    public async Task ExecuteAsync_Should_Execute_Registered_Tool()
    {
        _registry.Register(new CalculatorTool());

        var toolCall = new ToolCallBlock
        {
            Id = "call-1",
            Name = "calculator",
            Arguments = JsonSerializer.Deserialize<JsonElement>(@"{
                ""operation"": ""add"",
                ""a"": 5,
                ""b"": 3
            }")
        };

        var context = new ToolExecutionContext
        {
            AgentId = "test-agent",
            ToolCallId = "call-1"
        };

        var result = await _registry.ExecuteAsync(toolCall, context);

        Assert.NotNull(result);
        Assert.False(result.IsError);
        Assert.Contains("8", result.Content.OfType<TextBlock>().First().Text);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Return_Error_For_Unknown_Tool()
    {
        var toolCall = new ToolCallBlock
        {
            Id = "call-1",
            Name = "unknown_tool",
            Arguments = JsonSerializer.Deserialize<JsonElement>("{}")
        };

        var context = new ToolExecutionContext
        {
            AgentId = "test-agent",
            ToolCallId = "call-1"
        };

        var result = await _registry.ExecuteAsync(toolCall, context);

        Assert.NotNull(result);
        Assert.True(result.IsError);
        Assert.Contains("not found", result.Content.OfType<TextBlock>().First().Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Exclude_Should_Hide_And_Block_A_Tool_Registered_Before_Or_Afterward()
    {
        _registry.Register(new CalculatorTool());
        _registry.Exclude(["calculator"]);

        _registry.Register(new CalculatorTool());

        Assert.False(_registry.HasTool("calculator"));
        Assert.True(_registry.IsExcluded("calculator"));
        Assert.Contains("calculator", _registry.GetRegisteredToolNames());
        Assert.DoesNotContain(_registry.GetDefinitions(), definition => definition.Name == "calculator");
        Assert.Null(_registry.GetExecutor("calculator"));

        var result = await _registry.ExecuteAsync(new ToolCallBlock
        {
            Id = "call-1",
            Name = "calculator",
            Arguments = JsonSerializer.Deserialize<JsonElement>("{}")
        }, new ToolExecutionContext
        {
            AgentId = "test-agent",
            ToolCallId = "call-1"
        });

        Assert.True(result.IsError);
        Assert.Contains("excluded", result.Content.OfType<TextBlock>().First().Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegisterFunction_Should_Register_Delegate_Tool()
    {
        var parameters = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""name"": { ""type"": ""string"" }
            }
        }");

        _registry.RegisterFunction(
            "greet",
            "Greet someone",
            parameters,
            (args, ctx) => Task.FromResult(ToolResult.FromText($"Hello, {args["name"]}!"))
        );

        Assert.True(_registry.HasTool("greet"));
        var definitions = _registry.GetDefinitions();
        Assert.Contains(definitions, d => d.Name == "greet" && d.Description == "Greet someone");
    }

    [Fact]
    public async Task RegisterFunction_Should_Execute_Delegate()
    {
        var parameters = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""name"": { ""type"": ""string"" }
            }
        }");

        _registry.RegisterFunction(
            "greet",
            "Greet someone",
            parameters,
            (args, ctx) => Task.FromResult(ToolResult.FromText($"Hello, {args["name"]}!"))
        );

        var toolCall = new ToolCallBlock
        {
            Id = "call-1",
            Name = "greet",
            Arguments = JsonSerializer.Deserialize<JsonElement>(@"{""name"": ""World""}")
        };

        var context = new ToolExecutionContext
        {
            AgentId = "test-agent",
            ToolCallId = "call-1"
        };

        var result = await _registry.ExecuteAsync(toolCall, context);

        Assert.NotNull(result);
        Assert.False(result.IsError);
        Assert.Contains("Hello, World!", result.Content.OfType<TextBlock>().First().Text);
    }
}
