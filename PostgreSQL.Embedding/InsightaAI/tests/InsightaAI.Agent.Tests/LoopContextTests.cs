using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tests;

public sealed class LoopContextTests
{
    [Fact]
    public async Task AddMessageAsync_ShouldAwaitMessageAddedCallback()
    {
        var context = new LoopContext("session", "agent");
        var callbackCompleted = false;

        context.OnMessageAddedAsync = async _ =>
        {
            await Task.Yield();
            callbackCompleted = true;
        };

        await context.AddMessageAsync(Message.FromUser("persist me"));

        Assert.True(callbackCompleted);
        Assert.Single(context.Messages);
    }

    [Fact]
    public async Task AddMessagesAsync_ShouldAwaitCallbacksInMessageOrder()
    {
        var context = new LoopContext("session", "agent");
        var persistedMessages = new List<string>();
        context.OnMessageAddedAsync = message =>
        {
            persistedMessages.Add(Assert.IsType<TextBlock>(Assert.Single(message.Content)).Text);
            return Task.CompletedTask;
        };

        await context.AddMessagesAsync(
        [
            Message.FromUser("first"),
            Message.FromUser("second")
        ]);

        Assert.Equal(["first", "second"], persistedMessages);
    }
}
