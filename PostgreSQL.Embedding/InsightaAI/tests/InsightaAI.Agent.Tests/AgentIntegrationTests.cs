using InsightaAI.Agent.Models;
using InsightaAI.Agent.Tools;
using InsightaAI.LLM;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Anthropic;
using InsightaAI.LLM.Models;
using InsightaAI.LLM.OpenAI;

namespace InsightaAI.Agent.Tests;

/// <summary>
/// Agent 集成测试 - 使用真实 LLM API
/// </summary>
public class AgentIntegrationTests
{
    private readonly TestConfig _config;
    private readonly LlmClientFactory _factory;

    public AgentIntegrationTests()
    {
        _config = new TestConfig();
        _factory = new LlmClientFactory();
        _factory.RegisterAdapter(new OpenAIAdapter());
        _factory.RegisterAdapter(new AnthropicAdapter());
    }

    [Fact]
    public async Task Agent_Should_Work_With_OpenAI()
    {
        if (!_config.HasOpenAI || _config.SkipRealApiCalls)
        {
            return;
        }

        // Arrange
        var client = _factory.Create("openai", _config.GetOpenAIConfig()!);
        var toolRegistry = new ToolRegistry();
        toolRegistry.Register(new GetCurrentTimeTool());

        var config = new AgentConfig
        {
            Id = "openai-agent",
            Name = "OpenAI Agent",
            SystemPrompt = "You are a helpful assistant. Use tools when appropriate.",
            Model = _config.OpenAIModel,
            MaxToolRounds = 5
        };

        var agent = new Agent(config, client, toolRegistry);

        // Act
        Console.WriteLine("=== Agent with OpenAI ===");
        Console.WriteLine($"Model: {_config.OpenAIModel}");
        Console.WriteLine();

        AgentResult? result = null;
        await foreach (var evt in agent.RunStreamAsync("What time is it now?"))
        {
            switch (evt)
            {
                case AgentStartEvent start:
                    Console.WriteLine($"[AgentStart] Agent: {start.AgentName}, Model: {start.Model}");
                    break;

                case AgentRoundStartEvent roundStart:
                    Console.WriteLine($"\n--- Round {roundStart.Round} ---");
                    break;

                case AgentLlmStreamEvent llmEvent:
                    if (llmEvent.StreamEvent is TextDeltaEvent textDelta)
                    {
                        Console.Write(textDelta.Delta);
                    }
                    break;


                case AgentToolStartEvent toolStart:
                    Console.WriteLine($"\n>> Calling: {toolStart.ToolName}");
                    break;

                case AgentToolEndEvent toolEnd:
                    Console.WriteLine($"<< Result: {toolEnd.ResultPreview}");
                    break;

                case AgentCompleteEvent complete:
                    result = complete.Result;
                    Console.WriteLine($"\n[Complete] Rounds: {result.Rounds}, Duration: {result.DurationMs}ms");
                    break;
            }
        }

        // Assert
        Assert.NotNull(result);
        Assert.Equal(AgentStatus.Completed, result.Status);
        Assert.NotEmpty(result.Message.Content);
    }

    [Fact]
    public async Task Agent_Should_Work_With_Anthropic()
    {
        if (!_config.HasAnthropic || _config.SkipRealApiCalls)
        {
            return;
        }

        // Arrange
        var client = _factory.Create("anthropic", _config.GetAnthropicConfig()!);
        var toolRegistry = new ToolRegistry();
        toolRegistry.Register(new GetCurrentTimeTool());

        var config = new AgentConfig
        {
            Id = "anthropic-agent",
            Name = "Anthropic Agent",
            SystemPrompt = "You are a helpful assistant. Use tools when appropriate.",
            Model = _config.AnthropicModel,
            MaxToolRounds = 5
        };

        var agent = new Agent(config, client, toolRegistry);

        // Act
        Console.WriteLine("=== Agent with Anthropic ===");
        Console.WriteLine($"Model: {_config.AnthropicModel}");
        Console.WriteLine();

        AgentResult? result = null;
        await foreach (var evt in agent.RunStreamAsync("What time is it now?"))
        {
            switch (evt)
            {
                case AgentStartEvent start:
                    Console.WriteLine($"[AgentStart] Agent: {start.AgentName}, Model: {start.Model}");
                    break;

                case AgentRoundStartEvent roundStart:
                    Console.WriteLine($"\n--- Round {roundStart.Round} ---");
                    break;

                case AgentLlmStreamEvent llmEvent:
                    if (llmEvent.StreamEvent is TextDeltaEvent textDelta)
                    {
                        Console.Write(textDelta.Delta);
                    }
                    else if (llmEvent.StreamEvent is ThinkingDeltaEvent thinkingDelta)
                    {
                        Console.Write($"{thinkingDelta.Delta}");
                    }
                    break;

                case AgentToolStartEvent toolStart:
                    Console.WriteLine($"\n>> Calling: {toolStart.ToolName}");
                    break;

                case AgentToolEndEvent toolEnd:
                    Console.WriteLine($"<< Result: {toolEnd.ResultPreview}");
                    break;

                case AgentCompleteEvent complete:
                    result = complete.Result;
                    Console.WriteLine($"\n[Complete] Rounds: {result.Rounds}, Duration: {result.DurationMs}ms");
                    break;
            }
        }

        // Assert
        Assert.NotNull(result);
        Assert.Equal(AgentStatus.Completed, result.Status);
        Assert.NotEmpty(result.Message.Content);
    }

    [Fact]
    public async Task Agent_Should_Handle_Multiple_Tools()
    {
        if (!_config.HasOpenAI || _config.SkipRealApiCalls)
        {
            return;
        }

        // Arrange
        var client = _factory.Create("openai", _config.GetOpenAIConfig()!);
        var toolRegistry = new ToolRegistry();
        toolRegistry.Register(new GetCurrentTimeTool());
        toolRegistry.Register(new TerminateTool());

        var config = new AgentConfig
        {
            Id = "multi-tool-agent",
            Name = "Multi Tool Agent",
            SystemPrompt = @"You are a helpful assistant.
You have access to tools. When you have gathered enough information, use the terminate tool to provide your final answer.",
            Model = _config.OpenAIModel,
            MaxToolRounds = 5
        };

        var agent = new Agent(config, client, toolRegistry);

        // Act
        Console.WriteLine("=== Agent with Multiple Tools ===");
        Console.WriteLine();

        AgentResult? result = null;
        await foreach (var evt in agent.RunStreamAsync("What is the current date and time?"))
        {
            switch (evt)
            {
                case AgentStartEvent start:
                    Console.WriteLine($"[AgentStart] {start.AgentName}");
                    break;

                case AgentRoundStartEvent roundStart:
                    Console.WriteLine($"\n--- Round {roundStart.Round} ---");
                    break;

                case AgentLlmStreamEvent llmEvent:
                    if (llmEvent.StreamEvent is TextDeltaEvent textDelta)
                    {
                        Console.Write(textDelta.Delta);
                    }
                    break;

                case AgentToolStartEvent toolStart:
                    Console.WriteLine($"\n>> Calling: {toolStart.ToolName}");
                    break;

                case AgentToolEndEvent toolEnd:
                    Console.WriteLine($"<< Result: {toolEnd.ResultPreview}");
                    break;

                case AgentCompleteEvent complete:
                    result = complete.Result;
                    break;
            }
        }

        // Assert
        Assert.NotNull(result);
        Assert.Equal(AgentStatus.Completed, result.Status);
    }

    [Fact]
    public async Task Agent_Should_Execute_Single_Tool_With_Parameters()
    {
        if (!_config.HasOpenAI || _config.SkipRealApiCalls)
        {
            return;
        }

        // Arrange - 测试单个工具调用是否能正确传递参数
        var client = _factory.Create("openai", _config.GetOpenAIConfig()!);
        var toolRegistry = new ToolRegistry();
        toolRegistry.Register(new SaveNoteTool());

        var config = new AgentConfig
        {
            Id = "single-tool-agent",
            Name = "Single Tool Agent",
            SystemPrompt = "You are a helpful assistant. Use the save_note tool to save notes.",
            Model = _config.OpenAIModel,
            MaxToolRounds = 3
        };

        var agent = new Agent(config, client, toolRegistry);

        // Act
        Console.WriteLine("=== Single Tool Call Test ===");
        Console.WriteLine($"Model: {_config.OpenAIModel}");
        Console.WriteLine();

        AgentResult? result = null;

        await foreach (var evt in agent.RunStreamAsync("Save a note with key='test' and content='Hello World'"))
        {
            switch (evt)
            {
                case AgentStartEvent start:
                    Console.WriteLine($"[AgentStart] Agent: {start.AgentName}");
                    break;

                case AgentRoundStartEvent roundStart:
                    Console.WriteLine($"\n--- Round {roundStart.Round} ---");
                    break;

                case AgentLlmStreamEvent llmEvent:
                    if (llmEvent.StreamEvent is TextDeltaEvent textDelta)
                    {
                        Console.Write(textDelta.Delta);
                    }
                    break;

                case AgentToolStartEvent toolStart:
                    Console.WriteLine($"\n>> Calling: {toolStart.ToolName} (Id: {toolStart.ToolCallId})");
                    break;

                case AgentToolEndEvent toolEnd:
                    Console.WriteLine($"<< Result: {toolEnd.ResultPreview}");
                    break;

                case AgentCompleteEvent complete:
                    result = complete.Result;
                    Console.WriteLine($"\n[Complete] Rounds: {result.Rounds}, Duration: {result.DurationMs}ms");
                    break;
            }
        }

        // Assert
        Assert.NotNull(result);
        Assert.Equal(AgentStatus.Completed, result.Status);
    }

    [Fact]
    public async Task Agent_Should_Execute_Parallel_Tool_Calls()
    {
        if (!_config.HasOpenAI || _config.SkipRealApiCalls)
        {
            return;
        }

        // Arrange - 注册多个工具，让 LLM 可能同时调用
        var client = _factory.Create("openai", _config.GetOpenAIConfig()!);
        var toolRegistry = new ToolRegistry();
        toolRegistry.Register(new GetCurrentTimeTool());
        toolRegistry.Register(new SaveNoteTool());

        var config = new AgentConfig
        {
            Id = "parallel-agent",
            Name = "Parallel Tool Agent",
            SystemPrompt = @"You are a helpful assistant.
You have access to tools. When the user asks for multiple things, call ALL relevant tools at once in the same response.
For example, if asked to get time and save a note, call both tools simultaneously.",
            Model = _config.OpenAIModel,
            MaxToolRounds = 5,
            ParallelToolExecution = true  // 启用并行执行
        };

        var agent = new Agent(config, client, toolRegistry);

        // Act
        Console.WriteLine("=== Parallel Tool Calls Test ===");
        Console.WriteLine($"Model: {_config.OpenAIModel}");
        Console.WriteLine();

        AgentResult? result = null;
        var toolCallIds = new List<string>();

        await foreach (var evt in agent.RunStreamAsync("What time is it? Also save a note: key='meeting', content='Meeting at 3pm'"))
        {
            switch (evt)
            {
                case AgentStartEvent start:
                    Console.WriteLine($"[AgentStart] Agent: {start.AgentName}");
                    break;

                case AgentRoundStartEvent roundStart:
                    Console.WriteLine($"\n--- Round {roundStart.Round} ---");
                    break;

                case AgentLlmStreamEvent llmEvent:
                    if (llmEvent.StreamEvent is TextDeltaEvent textDelta)
                    {
                        Console.Write(textDelta.Delta);
                    }
                    break;

                case AgentToolStartEvent toolStart:
                    Console.WriteLine($"\n>> Calling: {toolStart.ToolName} (Id: {toolStart.ToolCallId})");
                    toolCallIds.Add(toolStart.ToolCallId!);
                    break;

                case AgentToolEndEvent toolEnd:
                    Console.WriteLine($"<< Result: {toolEnd.ResultPreview}");
                    break;

                case AgentCompleteEvent complete:
                    result = complete.Result;
                    Console.WriteLine($"\n[Complete] Rounds: {result.Rounds}, Duration: {result.DurationMs}ms");
                    break;
            }
        }

        // Assert
        Assert.NotNull(result);
        Assert.Equal(AgentStatus.Completed, result.Status);

        // 如果 LLM 同时调用了多个工具，应该在一轮内完成多个工具调用
        Console.WriteLine($"\nTotal tool calls: {toolCallIds.Count}");
        Console.WriteLine($"Tool call IDs: {string.Join(", ", toolCallIds)}");
    }

    [Fact]
    public async Task Agent_Should_Complete_Simple_Task_Without_Tools()
    {
        if (!_config.HasOpenAI || _config.SkipRealApiCalls)
        {
            return;
        }

        // Arrange
        var client = _factory.Create("openai", _config.GetOpenAIConfig()!);
        var toolRegistry = new ToolRegistry();

        var config = new AgentConfig
        {
            Id = "simple-agent",
            Name = "Simple Agent",
            SystemPrompt = "You are a helpful assistant.",
            Model = _config.OpenAIModel,
            MaxToolRounds = 1  // No tools, so 1 round is enough
        };

        var agent = new Agent(config, client, toolRegistry);

        // Act
        Console.WriteLine("=== Simple Task (No Tools) ===");
        Console.WriteLine();

        var result = await agent.RunAsync("Say 'Hello, World!' and nothing else.");

        Console.WriteLine($"Response: {result.Message.GetTextContent()}");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(AgentStatus.Completed, result.Status);
        Assert.Equal(1, result.Rounds);
        Assert.Contains("Hello", result.Message.GetTextContent(), StringComparison.OrdinalIgnoreCase);
    }
}
