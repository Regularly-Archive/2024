using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;

namespace InsightaAI.Agent.Tests;

public sealed class AgentEventHookFailureTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TurnEndHookFailure_Should_Not_Prevent_Completion_Or_Later_Hooks(bool synchronous)
    {
        using var llm = new MockLlmClient(response: "done");
        using var agent = new Agent(new AgentConfig
        {
            Id = "hook-failure-test", Name = "Test", Model = "mock"
        }, llm, new ToolRegistry());
        var observer = new CompletionHook();
        agent.AddAgentHook(new FailingHook(synchronous));
        agent.AddAgentHook(observer);

        var events = new List<AgentEvent>();
        await foreach (var item in agent.RunStreamAsync("hello"))
            events.Add(item);

        var completed = Assert.Single(events.OfType<AgentTurnEndEvent>());
        Assert.Equal(AgentStatus.Completed, completed.Result.Status);
        Assert.Equal("done", completed.Result.Message!.GetTextContent());
        Assert.Empty(events.OfType<AgentErrorEvent>());
        Assert.True(observer.Called);
    }

    private sealed class FailingHook(bool synchronous) : IAgentEventHook
    {
        public string Id => "failing";
        public Task OnAgentTurnEndedAsync(AgentEventHookContext context,
            IReadOnlyList<Message> messages, CancellationToken cancellationToken = default)
        {
            if (synchronous)
                throw new NullReferenceException("Synchronous hook failure");
            return Task.FromException(new NullReferenceException("Asynchronous hook failure"));
        }
    }

    private sealed class CompletionHook : IAgentEventHook
    {
        public string Id => "observer";
        public bool Called { get; private set; }
        public Task OnAgentTurnEndedAsync(AgentEventHookContext context,
            IReadOnlyList<Message> messages, CancellationToken cancellationToken = default)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }
}
