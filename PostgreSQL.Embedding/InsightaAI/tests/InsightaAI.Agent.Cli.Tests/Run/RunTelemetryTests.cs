using System.Diagnostics;
using InsightaAI.Agent.Diagnostics;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Models;

namespace InsightaAI.Agent.Cli.Tests;

[Collection("Run telemetry")]
public sealed class RunTelemetryTests
{
    [Fact]
    public async Task TurnEnd_Should_Tolerate_No_Listener()
    {
        var hook = new AgentEventTelemetryHook();
        await hook.OnAgentTurnEndedAsync(AgentEventHookContext.Create("session-1",
            new AgentTurnEndEvent
            {
                AgentId = "test",
                Result = new AgentResult { Status = AgentStatus.Completed }
            }), []);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TurnEnd_Should_Tolerate_Unsampled_Activity(bool sampleTurnEnd)
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "InsightaAI.Agent",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Name == "insighta.agent.turn_end" && !sampleTurnEnd
                    ? ActivitySamplingResult.None
                    : ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add
        };
        ActivitySource.AddActivityListener(listener);

        var agentId = Guid.NewGuid().ToString("N");
        var hook = new AgentEventTelemetryHook();
        hook.SetSessionContext(agentId, "test", "mock", "session-1");
        await hook.OnAgentRoundStartedAsync(AgentEventHookContext.Create("session-1",
            new AgentRoundStartEvent { AgentId = agentId, Round = 1 }), []);
        await hook.OnAgentTurnEndedAsync(AgentEventHookContext.Create("session-1",
            new AgentTurnEndEvent
            {
                AgentId = agentId,
                Result = new AgentResult { Status = AgentStatus.Completed, DurationMs = 42 }
            }), []);

        if (sampleTurnEnd)
        {
            var ended = Assert.Single(activities, activity => activity.OperationName == "insighta.agent.turn_end");
            Assert.Equal(42L, ended.GetTagItem("turn.duration_ms"));
            Assert.Equal("session-1", ended.GetTagItem("session.id"));
        }
        else
        {
            Assert.DoesNotContain(activities, activity => activity.OperationName == "insighta.agent.turn_end");
        }
    }
}

[CollectionDefinition("Run telemetry", DisableParallelization = true)]
public sealed class RunTelemetryCollection;
