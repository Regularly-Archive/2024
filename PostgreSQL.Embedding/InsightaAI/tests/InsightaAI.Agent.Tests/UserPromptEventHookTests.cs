using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;

namespace InsightaAI.Agent.Tests;

public sealed class UserPromptEventHookTests
{
    [Fact]
    public async Task UserPromptHook_ReceivesTheAcceptedUserMessageAndEventSnapshot()
    {
        using var llmClient = new MockLlmClient(response: "done");
        using var agent = new Agent(CreateConfig(), llmClient, new ToolRegistry());
        var hook = new CapturingUserPromptHook();
        agent.AddUserPromptHook(hook);

        await agent.RunAsync("Record this input.");

        var captured = await hook.Received.WaitAsync(TimeSpan.FromSeconds(2));
        var promptEvent = Assert.IsType<AgentUserPromptEvent>(captured.Context.Event);
        Assert.Equal("Record this input.", promptEvent.Input);
        Assert.Equal(MessageRole.User, captured.Message.Role);
        Assert.Equal("Record this input.", captured.Message.Content.OfType<TextBlock>().Single().Text);
    }

    private static AgentConfig CreateConfig() => new()
    {
        Id = "user-prompt-hook-agent",
        Name = "User Prompt Hook Agent",
        Model = "test-model"
    };

    private sealed class CapturingUserPromptHook : IUserPromptEventHook
    {
        private readonly TaskCompletionSource<(AgentEventHookContext Context, Message Message)> _received =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id => "capture-user-prompt";
        public Task<(AgentEventHookContext Context, Message Message)> Received => _received.Task;

        public Task OnUserPromptReceivedAsync(
            AgentEventHookContext context,
            Message userMessage,
            CancellationToken cancellationToken = default)
        {
            _received.TrySetResult((context, userMessage));
            return Task.CompletedTask;
        }
    }
}
