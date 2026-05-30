using System.Text.Json;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Tools;
using InsightaAI.LLM;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

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
        Assert.NotNull(result.Error);
        Assert.Contains("maximum tool rounds", result.Error);
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

        // Act - 扫描包含 StaticToolExamples 的程序集
        registry.ScanAssemblyContaining(typeof(StaticToolExamples));

        // Assert
        Assert.True(registry.HasTool("ask_user_static"));
        Assert.True(registry.HasTool("save_memory"));
        Assert.True(registry.HasTool("get_memory"));
    }

    [Fact]
    public void ToolScanner_Should_Build_Correct_Schema()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.ScanAssemblyContaining(typeof(StaticToolExamples));

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
        var askUserTool = new AskUserTool(question => Task.FromResult($"Answer: {question}"));

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

/// <summary>
/// Mock LLM Client for testing
/// </summary>
internal class MockLlmClient : ILlmClient
{
    private readonly string? _response;
    private readonly ToolCallBlock[]? _firstResponseToolCalls;
    private readonly string? _secondResponse;
    private readonly ToolCallBlock[]? _alwaysToolCalls;
    private int _callCount = 0;

    public string ProviderName => "mock";
    public bool SupportsReasoning => false;

    public MockLlmClient(
        string? response = null,
        ToolCallBlock[]? firstResponseToolCalls = null,
        string? secondResponse = null,
        ToolCallBlock[]? alwaysToolCalls = null)
    {
        _response = response ?? "Default response";
        _firstResponseToolCalls = firstResponseToolCalls;
        _secondResponse = secondResponse;
        _alwaysToolCalls = alwaysToolCalls;
    }

    public LlmStream Stream(LlmRequest request)
    {
        _callCount++;

        ToolCallBlock[]? toolCalls = null;
        string text;

        if (_alwaysToolCalls != null)
        {
            toolCalls = _alwaysToolCalls;
            text = "";
        }
        else if (_callCount == 1 && _firstResponseToolCalls != null)
        {
            toolCalls = _firstResponseToolCalls;
            text = "";
        }
        else
        {
            text = _callCount == 1 ? _response : (_secondResponse ?? _response);
        }

        return new MockLlmStream(text, toolCalls);
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        _callCount++;
        var text = _callCount == 1 ? _response : (_secondResponse ?? _response);

        var content = new List<ContentBlock> { new TextBlock { Text = text } };

        if (_firstResponseToolCalls != null && _callCount == 1)
        {
            content.AddRange(_firstResponseToolCalls);
        }

        return Task.FromResult(new LlmResponse
        {
            Model = request.Model,
            Content = content.ToArray(),
            FinishReason = _firstResponseToolCalls != null && _callCount == 1
                ? DoneReason.ToolCalls
                : DoneReason.Complete,
            Usage = new TokenUsage { InputTokens = 10, OutputTokens = 20 }
        });
    }
}

/// <summary>
/// Mock LLM Stream for testing
/// </summary>
internal class MockLlmStream : LlmStream
{
    private readonly string _text;
    private readonly ToolCallBlock[]? _toolCalls;

    public bool IsCompleted { get; private set; }
    public bool IsAborted { get; private set; }

    public MockLlmStream(string text, ToolCallBlock[]? toolCalls = null)
    {
        _text = text;
        _toolCalls = toolCalls;
    }

    public void Abort()
    {
        IsAborted = true;
    }

    public async IAsyncEnumerable<StreamEvent> GetStreamEventsAsync()
    {
        yield return new StreamStartEvent { Model = "test-model", Provider = "mock" };

        if (!string.IsNullOrEmpty(_text))
        {
            yield return new TextDeltaEvent { Delta = _text, ContentIndex = 0 };
        }

        if (_toolCalls != null)
        {
            foreach (var toolCall in _toolCalls)
            {
                yield return new ToolCallStartEvent
                {
                    ContentIndex = 0,
                    ToolName = toolCall.Name,
                    ToolCallId = toolCall.Id
                };
                yield return new ToolCallDeltaEvent
                {
                    ContentIndex = 0,
                    ArgumentsDelta = toolCall.Arguments.GetRawText()
                };
            }
        }

        yield return new DoneEvent
        {
            Reason = _toolCalls?.Length > 0 ? DoneReason.ToolCalls : DoneReason.Complete
        };

        IsCompleted = true;
    }

    public IAsyncEnumerator<StreamEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return GetStreamEventsAsync().GetAsyncEnumerator(cancellationToken);
    }

    public Task<LlmResponse> GetResponseAsync(CancellationToken cancellationToken = default)
    {
        var content = new List<ContentBlock>();

        if (!string.IsNullOrEmpty(_text))
        {
            content.Add(new TextBlock { Text = _text });
        }

        if (_toolCalls != null)
        {
            content.AddRange(_toolCalls);
        }

        return Task.FromResult(new LlmResponse
        {
            Model = "test-model",
            Content = content.ToArray(),
            FinishReason = _toolCalls?.Length > 0 ? DoneReason.ToolCalls : DoneReason.Complete,
            Usage = new TokenUsage { InputTokens = 10, OutputTokens = 20 }
        });
    }
}
