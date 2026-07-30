using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tests;

public sealed class AgentErrorLifecycleTests
{
    [Fact]
    public async Task LlmError_MapsToSingleAgentErrorAndFailedTurn()
    {
        using var agent = new Agent(CreateConfig(), new ErrorLlmClient(), new ToolRegistry());
        var hook = new CapturingErrorHook();
        agent.AddAgentHook(hook);

        var events = new List<AgentEvent>();
        await foreach (var @event in agent.RunStreamAsync("Trigger an error."))
            events.Add(@event);

        var error = Assert.Single(events.OfType<AgentErrorEvent>());
        Assert.Equal("simulated provider failure", error.ErrorMessage);
        Assert.False(error.Recoverable);
        Assert.DoesNotContain(events.OfType<AgentLlmStreamEvent>(),
            @event => @event.StreamEvent is ErrorEvent);

        var turnEnd = Assert.Single(events.OfType<AgentTurnEndEvent>());
        Assert.Equal(AgentStatus.Failed, turnEnd.Result.Status);
        Assert.Null(turnEnd.Result.Message);
        Assert.Equal("simulated provider failure", turnEnd.Result.Error);

        var hookEvent = await hook.Received.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Same(error, hookEvent);
    }

    private static AgentConfig CreateConfig() => new()
    {
        Id = "error-agent",
        Name = "Error Agent",
        Model = "test-model"
    };

    private sealed class CapturingErrorHook : IAgentEventHook
    {
        private readonly TaskCompletionSource<AgentErrorEvent> _received =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id => "capture-error";
        public Task<AgentErrorEvent> Received => _received.Task;

        public Task OnAgentErrorAsync(AgentEventHookContext context,
            CancellationToken cancellationToken = default)
        {
            _received.TrySetResult(context.GetEvent<AgentErrorEvent>());
            return Task.CompletedTask;
        }
    }

    private sealed class ErrorLlmClient : ILlmClient
    {
        public string AdapterName => "error-test";
        public bool SupportsReasoning => false;

        public LlmStream Streaming(LlmRequest request) => new ErrorLlmStream();

        public Task<LlmResponse> CompleteAsync(LlmRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LlmResponse
            {
                Model = request.Model,
                Content = [],
                FinishReason = DoneReason.Error
            });

        public void Dispose()
        {
        }
    }

    private sealed class ErrorLlmStream : LlmStream
    {
        public bool IsCompleted { get; private set; }
        public bool IsAborted { get; private set; }

        public async IAsyncEnumerator<StreamEvent> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            yield return new ErrorEvent
            {
                Error = new InvalidOperationException("simulated provider failure"),
                Recoverable = false
            };
            yield return new DoneEvent { Reason = DoneReason.Error };
            IsCompleted = true;
            await Task.CompletedTask;
        }

        public Task<LlmResponse> GetResponseAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LlmResponse
            {
                Model = "test-model",
                Content = [],
                FinishReason = DoneReason.Error
            });

        public void Abort() => IsAborted = true;
        public void Dispose()
        {
        }
    }
}
