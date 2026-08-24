using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Harness.Local;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Tools;
using InsightaAI.Agent.Tools.BuiltIn;
using InsightaAI.Agent.Storage;
using InsightaAI.LLM.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace InsightaAI.Agent.Tests.Tools;

public sealed class ToolResultProcessorTests
{
    [Fact]
    public async Task ProcessAsync_Should_PersistRedactedContentBeforeCreatingPreview()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"insighta-test-{Guid.NewGuid():N}");
        try
        {
            const string secret = "do-not-persist-this-password";
            var original = $"Password={secret};\n" +
                string.Join("\n", Enumerable.Range(1, 2000).Select(i => $"line {i}: {new string('x', 32)}"));
            var metadata = new Dictionary<string, object?> { ["source"] = "test" };
            var result = new ToolResult
            {
                Content = [new TextBlock { Text = original }],
                IsError = true,
                Metadata = metadata
            };
            var processor = new ToolResultProcessor(
                new ToolRegistry(), new ToolResultArtifactStore(new LocalFileSystem(), directory));

            var processed = await processor.ProcessAsync(
                "session-1", CreateToolCall("custom_tool"), result, enabled: true);

            Assert.Equal(ToolResultRetentionLevel.Preview, processed.State.RetentionLevel);
            Assert.NotNull(processed.State.Artifact);
            Assert.True(processed.Result.IsError);
            Assert.Same(metadata, processed.Result.Metadata);
            var persisted = await File.ReadAllTextAsync(processed.State.Artifact.Path);
            Assert.DoesNotContain(secret, persisted);
            Assert.Contains("[REDACTED]", persisted);
            Assert.DoesNotContain(secret, processed.Result.Content.OfType<TextBlock>().Single().Text);
            Assert.True(processed.CurrentLength < original.Length);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessAsync_Should_KeepSmallResultFull()
    {
        var processor = new ToolResultProcessor(
            new ToolRegistry(), new ToolResultArtifactStore(new LocalFileSystem(), Path.GetTempPath()));

        var processed = await processor.ProcessAsync(
            "session-1", CreateToolCall("custom_tool"), ToolResult.FromText("small result"), enabled: true);

        Assert.Equal(ToolResultRetentionLevel.Full, processed.State.RetentionLevel);
        Assert.Null(processed.State.Artifact);
        Assert.Equal("small result", processed.Result.Content.OfType<TextBlock>().Single().Text);
    }

    [Fact]
    public async Task ProcessAsync_Should_PersistSmallDelegateResultWhenToolPrefersPersistence()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"insighta-test-{Guid.NewGuid():N}");
        try
        {
            var registry = new ToolRegistry();
            registry.Register(new DelegateTool(new FixedDelegationHandler()));
            var processor = new ToolResultProcessor(
                registry, new ToolResultArtifactStore(new LocalFileSystem(), directory));

            var processed = await processor.ProcessAsync(
                "session-1", CreateToolCall("delegate"), ToolResult.FromText("complete review report"), enabled: true);

            Assert.Equal(ToolResultRetentionLevel.Preview, processed.State.RetentionLevel);
            Assert.NotNull(processed.State.Artifact);
            Assert.Equal("complete review report", await File.ReadAllTextAsync(processed.State.Artifact.Path));
            Assert.Contains("Full output saved as artifact", processed.Result.Content.OfType<TextBlock>().Single().Text);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessAsync_Should_NotPersistPreferredResultWhenPersistenceIsDisabled()
    {
        var registry = new ToolRegistry();
        registry.Register(new DelegateTool(new FixedDelegationHandler()));
        var processor = new ToolResultProcessor(
            registry, new ToolResultArtifactStore(new LocalFileSystem(), Path.GetTempPath()));

        var processed = await processor.ProcessAsync(
            "session-1", CreateToolCall("delegate"), ToolResult.FromText("complete review report"), enabled: false);

        Assert.Equal(ToolResultRetentionLevel.Full, processed.State.RetentionLevel);
        Assert.Null(processed.State.Artifact);
    }

    [Fact]
    public async Task ToolEndPreview_Should_UseRedactedResult()
    {
        const string secret = "preview-secret";
        var services = new ServiceCollection();
        services.AddSingleton(new ToolRegistry());
        using var serviceProvider = services.BuildServiceProvider();
        var executor = new ToolCallExecutor(
            "agent-1",
            "session-1",
            (_, _) => Task.FromResult(new ToolCallResponse(true, ToolResult.FromText($"Password={secret}"))),
            serviceProvider);

        var events = new List<AgentEvent>();
        await foreach (var @event in executor.ExecuteToolsSequentialAsync([CreateToolCall("bash")], CancellationToken.None))
        {
            events.Add(@event);
        }

        var toolEnd = Assert.Single(events.OfType<AgentToolEndEvent>());
        Assert.DoesNotContain(secret, toolEnd.ResultPreview);
        Assert.Contains("[REDACTED]", toolEnd.ResultPreview);
        Assert.DoesNotContain(secret, executor.Results.Single().Result.Content.OfType<TextBlock>().Single().Text);
    }

    [Fact]
    public async Task ToolProgress_ShouldBeRedactedBeforeItLeavesTheToolExecutor()
    {
        const string secret = "progress-secret";
        var services = new ServiceCollection();
        services.AddSingleton(new ToolRegistry());
        using var serviceProvider = services.BuildServiceProvider();
        var executor = new ToolCallExecutor(
            "agent-1",
            "session-1",
            async (request, cancellationToken) =>
            {
                await request.Progress.ReportAsync(new ToolProgressUpdate
                {
                    Kind = ToolProgressKind.Output,
                    Text = $"Password={secret}"
                }, cancellationToken);
                return new ToolCallResponse(true, ToolResult.FromText("done"));
            },
            serviceProvider);

        var events = new List<AgentEvent>();
        await foreach (var @event in executor.ExecuteToolsSequentialAsync([CreateToolCall("bash")], CancellationToken.None))
            events.Add(@event);

        var progress = Assert.Single(events.OfType<AgentToolProgressEvent>());
        Assert.DoesNotContain(secret, progress.Progress.Text);
        Assert.Contains("[REDACTED]", progress.Progress.Text);
    }

    [Fact]
    public async Task SequentialExecution_Should_NotStartNextToolUntilConsumerAdvancesPastPreviousToolEnd()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ToolRegistry());
        using var serviceProvider = services.BuildServiceProvider();
        var secondToolStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new ToolCallExecutor(
            "agent-1",
            "session-1",
            (request, _) =>
            {
                if (request.ToolCall.Name == "second")
                    secondToolStarted.TrySetResult();

                return Task.FromResult(new ToolCallResponse(true, ToolResult.FromText("done")));
            },
            serviceProvider);

        var calls = new[]
        {
            CreateToolCall("first", "call-1"),
            CreateToolCall("second", "call-2")
        };
        await using var events = executor.ExecuteToolsSequentialAsync(calls, CancellationToken.None)
            .GetAsyncEnumerator();

        Assert.True(await events.MoveNextAsync());
        Assert.IsType<AgentToolStartEvent>(events.Current);
        Assert.True(await events.MoveNextAsync());
        Assert.IsType<AgentToolEndEvent>(events.Current);

        Assert.False(secondToolStarted.Task.IsCompleted);

        Assert.True(await events.MoveNextAsync());
        Assert.IsType<AgentToolStartEvent>(events.Current);
        await secondToolStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        while (await events.MoveNextAsync())
        {
        }
    }

    [Fact]
    public void MessageConverters_Should_RoundTripToolResultState()
    {
        var state = new ToolResultState
        {
            RetentionLevel = ToolResultRetentionLevel.Placeholder,
            OriginalLength = 42_000,
            CanReplay = true,
            MinimumLevel = ToolResultRetentionLevel.Removed,
            Artifact = new ToolResultArtifactInfo
            {
                Id = "artifact-1",
                Path = "tool_results/artifact-1.txt",
                ByteSize = 42_000
            }
        };
        var message = new Message
        {
            Role = MessageRole.ToolResult,
            ToolCallId = "call-1",
            ToolName = "read_file",
            Content = [new TextBlock { Text = "[omitted]" }],
            ToolResultState = state
        };

        var restored = message.ToMessageRecord("session-1").ToLlmMessage();

        Assert.Equal(state, restored.ToolResultState);
    }

    private static ToolCallBlock CreateToolCall(string toolName, string id = "call-1") => new()
    {
        Id = id,
        Name = toolName,
        Arguments = JsonDocument.Parse("{}").RootElement.Clone()
    };

    private sealed class FixedDelegationHandler : IAgentDelegationHandler
    {
        public Task<ToolResult> DelegateAsync(AgentDelegationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ToolResult.FromText("unused"));
    }
}
