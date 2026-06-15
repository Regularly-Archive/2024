using System.Text.Json;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Tools;
using InsightaAI.LLM;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;

namespace InsightaAI.Agent.Tests;

/// <summary>
/// Agent 单元测试
/// </summary>
public class AgentTests
{
    private readonly ToolRegistry _toolRegistry;

    public AgentTests()
    {
        _toolRegistry = new ToolRegistry();
        _toolRegistry.Register(new GetCurrentTimeTool());
        _toolRegistry.Register(new TerminateTool());
    }

    [Fact]
    public void Agent_Should_Be_Created_With_Valid_Config()
    {
        // Arrange
        var config = CreateConfig();
        var llmClient = new MockLlmClient();
        var toolRegistry = new ToolRegistry();

        // Act
        var agent = new Agent(config, llmClient, toolRegistry);

        // Assert
        Assert.NotNull(agent);
        Assert.Equal("test-agent", agent.Config.Id);
        Assert.Equal("Test Agent", agent.Config.Name);
    }

    [Fact]
    public void Agent_Should_Throw_When_Config_Is_Null()
    {
        // Arrange
        var llmClient = new MockLlmClient();
        var toolRegistry = new ToolRegistry();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new Agent(null!, llmClient, toolRegistry));
    }

    [Fact]
    public void Agent_Should_Throw_When_LlmClient_Is_Null()
    {
        // Arrange
        var config = CreateConfig();
        var toolRegistry = new ToolRegistry();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new Agent(config, null!, toolRegistry));
    }

    [Fact]
    public void AgentConfig_Should_Have_Default_MaxToolRounds()
    {
        // Arrange & Act
        var config = CreateConfig();

        // Assert
        Assert.Equal(15, config.MaxToolRounds);
    }

    [Fact]
    public void AgentConfig_Should_Allow_Custom_MaxToolRounds()
    {
        // Arrange & Act
        var config = CreateConfig(maxToolRounds: 5);

        // Assert
        Assert.Equal(5, config.MaxToolRounds);
    }

    [Fact]
    public void TerminateTool_Should_Be_Registered()
    {
        // Assert
        Assert.True(_toolRegistry.HasTool("terminate"));
    }

    [Fact]
    public void GetCurrentTimeTool_Should_Be_Registered()
    {
        // Assert
        Assert.True(_toolRegistry.HasTool("get_current_time"));
    }

    [Fact]
    public void TerminateTool_Should_Return_Answer()
    {
        // Arrange
        var tool = new TerminateTool();
        var args = new Dictionary<string, object> { { "answer", "Task completed successfully." } };
        var context = new ToolExecutionContext
        {
            AgentId = "test",
            ToolCallId = "call-1"
        };

        // Act
        var result = tool.ExecuteAsync(args, context).Result;

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsError);
        var text = result.Content.OfType<TextBlock>().First().Text;
        Assert.Contains("[TERMINATE]", text);
        Assert.Contains("Task completed successfully.", text);
    }

    [Fact]
    public void GetCurrentTimeTool_Should_Return_Time()
    {
        // Arrange
        var tool = new GetCurrentTimeTool();
        var args = new Dictionary<string, object>();
        var context = new ToolExecutionContext
        {
            AgentId = "test",
            ToolCallId = "call-1"
        };

        // Act
        var result = tool.ExecuteAsync(args, context).Result;

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsError);
        var text = result.Content.OfType<TextBlock>().First().Text;
        Assert.Contains("-", text); // Date format contains dashes
    }

    [Fact]
    public async Task Agent_Should_Complete_Simple_Task()
    {
        // Arrange
        var config = CreateConfig();
        var llmClient = new MockLlmClient(response: "Hello, World!");
        var toolRegistry = new ToolRegistry();

        var agent = new Agent(config, llmClient, toolRegistry);

        // Act
        var result = await agent.RunAsync("Say hello");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(AgentStatus.Completed, result.Status);
        Assert.Equal(1, result.Rounds);
        Assert.Contains("Hello, World!", result.Message.Content.OfType<TextBlock>().First().Text);
    }

    [Fact]
    public async Task Agent_Should_Handle_Tool_Calls()
    {
        // Arrange
        var config = CreateConfig();
        var toolCall = new ToolCallBlock
        {
            Id = "call-1",
            Name = "get_current_time",
            Arguments = JsonSerializer.Deserialize<JsonElement>("{}")
        };

        var llmClient = new MockLlmClient(
            firstResponseToolCalls: [toolCall],
            secondResponse: "The current time has been retrieved."
        );

        var toolRegistry = new ToolRegistry();
        toolRegistry.Register(new GetCurrentTimeTool());

        var agent = new Agent(config, llmClient, toolRegistry);

        // Act
        var result = await agent.RunAsync("What time is it?");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(AgentStatus.Completed, result.Status);
        Assert.Equal(2, result.Rounds);
    }

    [Fact]
    public async Task Agent_Should_Respect_MaxToolRounds()
    {
        // Arrange
        var config = CreateConfig(maxToolRounds: 3);
        var toolCall = new ToolCallBlock
        {
            Id = "call-1",
            Name = "get_current_time",
            Arguments = JsonSerializer.Deserialize<JsonElement>("{}")
        };

        // Always return tool calls to force max rounds
        var llmClient = new MockLlmClient(alwaysToolCalls: [toolCall]);

        var toolRegistry = new ToolRegistry();
        toolRegistry.Register(new GetCurrentTimeTool());

        var agent = new Agent(config, llmClient, toolRegistry);

        // Act
        var result = await agent.RunAsync("Keep getting time");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Rounds);
        // 现在超过最大轮次时，Agent 会调用 LLM 生成总结，而不是返回错误
        Assert.NotNull(result.Message);
        Assert.Equal(AgentStatus.Completed, result.Status);
    }

    [Fact]
    public async Task Agent_Should_Emit_Events()
    {
        // Arrange
        var config = CreateConfig();
        var llmClient = new MockLlmClient(response: "Done");
        var toolRegistry = new ToolRegistry();
        var agent = new Agent(config, llmClient, toolRegistry);

        var events = new List<AgentEvent>();

        // Act
        await foreach (var evt in agent.RunStreamAsync("Test"))
        {
            events.Add(evt);
        }

        // Assert
        Assert.NotEmpty(events);
        Assert.IsType<AgentStartEvent>(events[0]);
        Assert.IsType<AgentCompleteEvent>(events.Last());

        // Should have: Start, RoundStart, LlmStream(s), RoundEnd, Complete
        Assert.Contains(events, e => e.Type == AgentEventType.Start);
        Assert.Contains(events, e => e.Type == AgentEventType.RoundStart);
        Assert.Contains(events, e => e.Type == AgentEventType.LlmStream);
        Assert.Contains(events, e => e.Type == AgentEventType.RoundEnd);
        Assert.Contains(events, e => e.Type == AgentEventType.Complete);
    }

    [Fact]
    public async Task Agent_Should_Execute_Parallel_Tool_Calls()
    {
        // Arrange - Two tool calls in first response
        var config = CreateConfig(parallelToolExecution: true);
        var toolCalls = new ToolCallBlock[]
        {
            new()
            {
                Id = "call-1",
                Name = "get_current_time",
                Arguments = JsonSerializer.Deserialize<JsonElement>("{}")
            },
            new()
            {
                Id = "call-2",
                Name = "terminate",
                Arguments = JsonSerializer.Deserialize<JsonElement>(@"{""answer"":""Done""}")
            }
        };

        var llmClient = new MockLlmClient(
            firstResponseToolCalls: toolCalls,
            secondResponse: "Both tools executed."
        );

        var toolRegistry = new ToolRegistry();
        toolRegistry.Register(new GetCurrentTimeTool());
        toolRegistry.Register(new TerminateTool());

        var agent = new Agent(config, llmClient, toolRegistry);
        var events = new List<AgentEvent>();

        // Act
        await foreach (var evt in agent.RunStreamAsync("Get time and terminate"))
        {
            events.Add(evt);
        }

        // Assert - Both tool start/end events should be present
        var toolStartEvents = events.OfType<AgentToolStartEvent>().ToList();
        var toolEndEvents = events.OfType<AgentToolEndEvent>().ToList();

        Assert.Equal(2, toolStartEvents.Count);
        Assert.Equal(2, toolEndEvents.Count);
        Assert.Contains(toolStartEvents, e => e.ToolCallId == "call-1");
        Assert.Contains(toolStartEvents, e => e.ToolCallId == "call-2");
        Assert.Contains(toolEndEvents, e => e.ToolCallId == "call-1");
        Assert.Contains(toolEndEvents, e => e.ToolCallId == "call-2");
    }

    [Fact]
    public async Task Agent_Should_Execute_Sequential_When_Parallel_Disabled()
    {
        // Arrange - Two tool calls, but parallel disabled
        var config = CreateConfig(parallelToolExecution: false);
        var toolCalls = new ToolCallBlock[]
        {
            new()
            {
                Id = "call-1",
                Name = "get_current_time",
                Arguments = JsonSerializer.Deserialize<JsonElement>("{}")
            },
            new()
            {
                Id = "call-2",
                Name = "terminate",
                Arguments = JsonSerializer.Deserialize<JsonElement>(@"{""answer"":""Done""}")
            }
        };

        var llmClient = new MockLlmClient(
            firstResponseToolCalls: toolCalls,
            secondResponse: "Both tools executed."
        );

        var toolRegistry = new ToolRegistry();
        toolRegistry.Register(new GetCurrentTimeTool());
        toolRegistry.Register(new TerminateTool());

        var agent = new Agent(config, llmClient, toolRegistry);
        var events = new List<AgentEvent>();

        // Act
        await foreach (var evt in agent.RunStreamAsync("Get time and terminate"))
        {
            events.Add(evt);
        }

        // Assert - Both tool start/end events should be present (sequential)
        var toolStartEvents = events.OfType<AgentToolStartEvent>().ToList();
        var toolEndEvents = events.OfType<AgentToolEndEvent>().ToList();

        Assert.Equal(2, toolStartEvents.Count);
        Assert.Equal(2, toolEndEvents.Count);

        // In sequential mode, events should be in order: call-1 start, call-1 end, call-2 start, call-2 end
        Assert.Equal("call-1", toolStartEvents[0].ToolCallId);
        Assert.Equal("call-1", toolEndEvents[0].ToolCallId);
        Assert.Equal("call-2", toolStartEvents[1].ToolCallId);
        Assert.Equal("call-2", toolEndEvents[1].ToolCallId);
    }

    [Fact]
    public void ToolScanner_Should_Scan_Static_Methods()
    {
        // Arrange
        var registry = new ToolRegistry();

        // Act - 扫描包含 TestStaticTools 的程序集
        registry.ScanAssemblyContaining(typeof(TestStaticTools));

        // Assert
        Assert.True(registry.HasTool("save_memory"));
        Assert.True(registry.HasTool("get_memory"));
    }

    [Fact]
    public void ToolScanner_Should_Build_Correct_Schema()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.ScanAssemblyContaining(typeof(TestStaticTools));

        // Act
        var definitions = registry.GetDefinitions();
        var saveMemoryDef = definitions.FirstOrDefault(d => d.Name == "save_memory");

        // Assert
        Assert.NotNull(saveMemoryDef);
        Assert.Contains("key", saveMemoryDef.Schema.GetRawText());
        Assert.Contains("content", saveMemoryDef.Schema.GetRawText());
    }

    [Fact]
    public void ToolScanner_Should_Register_Instance_Methods()
    {
        // Arrange
        var registry = new ToolRegistry();
        var askUserTool = new AskUserTool((question, choices, multi) => Task.FromResult($"Answer: {question}"));

        // Act
        registry.Register(askUserTool);

        // Assert
        Assert.True(registry.HasTool("ask_user"));
    }

    // ============================================================
    // Helper methods
    // ============================================================

    private static AgentConfig CreateConfig(int maxToolRounds = 15, bool parallelToolExecution = true) => new()
    {
        Id = "test-agent",
        Name = "Test Agent",
        SystemPrompt = "You are a helpful assistant.",
        Model = "test-model",
        MaxToolRounds = maxToolRounds,
        ParallelToolExecution = parallelToolExecution
    };
}
