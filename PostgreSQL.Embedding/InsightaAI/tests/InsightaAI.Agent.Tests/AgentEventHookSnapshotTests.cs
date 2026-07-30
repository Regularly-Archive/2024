using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;

namespace InsightaAI.Agent.Tests;

public sealed class AgentEventHookSnapshotTests
{
    [Fact]
    public async Task BackgroundHooks_KeepTheirTriggeringEventSnapshot()
    {
        using var llmClient = new MockLlmClient(response: "done");
        using var agent = new Agent(CreateConfig(), llmClient, new ToolRegistry());
        var hook = new DelayedSnapshotHook(expectedSnapshots: 3);
        agent.AddAgentHook(hook);

        await agent.RunAsync("Test hook event snapshots.");
        hook.Release();

        var snapshots = await hook.Snapshots.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Collection(snapshots,
            @event => Assert.IsType<AgentTurnStartEvent>(@event),
            @event => Assert.IsType<AgentRoundStartEvent>(@event),
            @event => Assert.IsType<AgentRoundEndEvent>(@event));
    }

    private static AgentConfig CreateConfig() => new()
    {
        Id = "hook-snapshot-agent",
        Name = "Hook Snapshot Agent",
        Model = "test-model"
    };

    private sealed class DelayedSnapshotHook(int expectedSnapshots) : IAgentEventHook
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<IReadOnlyList<AgentEvent>> _snapshots = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<AgentEvent> _events = [];

        public string Id => "delayed-snapshot";
        public Task<IReadOnlyList<AgentEvent>> Snapshots => _snapshots.Task;

        public Task OnAgentTurnStartedAsync(AgentEventHookContext context, string message,
            CancellationToken cancellationToken = default) => CaptureAsync(context);

        public Task OnAgentRoundStartedAsync(AgentEventHookContext context, IReadOnlyList<Message> messages,
            CancellationToken cancellationToken = default) => CaptureAsync(context);

        public Task OnAgentRoundEndedAsync(AgentEventHookContext context, IReadOnlyList<Message> messages,
            Message? assistantMessage, CancellationToken cancellationToken = default) => CaptureAsync(context);

        public void Release() => _release.TrySetResult();

        private async Task CaptureAsync(AgentEventHookContext context)
        {
            await _release.Task;

            lock (_events)
            {
                _events.Add(context.Event);
                if (_events.Count == expectedSnapshots)
                    _snapshots.TrySetResult(_events.ToArray());
            }
        }
    }
}
