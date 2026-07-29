using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InsightaAI.Agent.Tests;

public sealed class AgentBuilderTests
{
    [Fact]
    public void Build_WithoutToolRegistry_UsesDefaultRegistry()
    {
        using var llmClient = new MockLlmClient(response: "done");
        var config = CreateConfig();

        using var agent = new AgentBuilder(config)
            .WithLlm(llmClient)
            .Build();

        Assert.Same(config, agent.Config);
    }

    [Fact]
    public async Task ConfigureServices_ExposesServices_AndAgentDisposalDisposesThem()
    {
        var probe = new DisposableProbe();
        var resolved = false;
        var toolRegistry = CreateToolRegistry("inspect_service", (_, context) =>
        {
            resolved = context.Services?.GetRequiredService<DisposableProbe>() is not null;
            return Task.FromResult(ToolResult.FromText("inspected"));
        });

        using var llmClient = new MockLlmClient(
            firstResponseToolCalls: [CreateToolCall("inspect_service")],
            secondResponse: "done");
        var config = CreateConfig();

        using (var agent = new AgentBuilder(config)
            .WithLlm(llmClient)
            .WithToolRegistry(toolRegistry)
            .ConfigureServices(services => services.AddSingleton<DisposableProbe>(_ => probe))
            .Build())
        {
            var result = await agent.RunAsync("Inspect the configured service.");
            Assert.Equal(AgentStatus.Completed, result.Status);
        }

        Assert.True(resolved);
        Assert.True(probe.IsDisposed);
    }

    [Fact]
    public void Build_WithoutLlm_ThrowsHelpfulError()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AgentBuilder(CreateConfig()).Build());

        Assert.Contains("WithLlm", exception.Message);
    }

    [Fact]
    public async Task WithToolRegistry_ReplacesDefaultRegistry()
    {
        var invoked = false;
        var toolRegistry = CreateToolRegistry("custom_tool", (_, _) =>
        {
            invoked = true;
            return Task.FromResult(ToolResult.FromText("custom"));
        });

        using var llmClient = new MockLlmClient(
            firstResponseToolCalls: [CreateToolCall("custom_tool")],
            secondResponse: "done");

        using var agent = new AgentBuilder(CreateConfig())
            .WithLlm(llmClient)
            .WithToolRegistry(toolRegistry)
            .Build();

        var result = await agent.RunAsync("Use the custom tool.");

        Assert.Equal(AgentStatus.Completed, result.Status);
        Assert.True(invoked);
    }

    [Fact]
    public async Task WithLoggerFactory_LogsAgentLifecycleEvents()
    {
        var loggerProvider = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
        using var llmClient = new MockLlmClient(response: "done");

        using var agent = new AgentBuilder(CreateConfig())
            .WithLlm(llmClient)
            .WithLoggerFactory(loggerFactory)
            .Build();

        var result = await agent.RunAsync("Write a log entry.");

        Assert.Equal(AgentStatus.Completed, result.Status);
        Assert.Contains(loggerProvider.Messages, message => message.Contains("Turn started"));
        Assert.Contains(loggerProvider.Messages, message => message.Contains("Turn ended"));
    }

    private static AgentConfig CreateConfig() => new()
    {
        Id = "builder-test-agent",
        Name = "Builder Test Agent",
        Model = "test-model",
        MaxToolRounds = 2
    };

    private static ToolRegistry CreateToolRegistry(
        string name,
        Func<IDictionary<string, object>, ToolExecutionContext, Task<ToolResult>> handler)
    {
        return new ToolRegistry().RegisterFunction(
            name,
            "A test tool.",
            JsonDocument.Parse("{}").RootElement.Clone(),
            handler);
    }

    private static ToolCallBlock CreateToolCall(string name) => new()
    {
        Id = $"call-{name}",
        Name = name,
        Arguments = JsonDocument.Parse("{}").RootElement.Clone()
    };

    private sealed class DisposableProbe : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            messages.Add(formatter(state, exception));
        }
    }
}
