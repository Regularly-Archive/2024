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
    public async Task ToolEndPreview_Should_UseRedactedResult()
    {
        const string secret = "preview-secret";
        var services = new ServiceCollection();
        services.AddSingleton(new ToolRegistry());
        using var serviceProvider = services.BuildServiceProvider();
        var executor = new ToolCallExecutor(
            "agent-1",
            "session-1",
            (_, _) => Task.FromResult(new ToolCallReponse(true, ToolResult.FromText($"Password={secret}"))),
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

    private static ToolCallBlock CreateToolCall(string toolName) => new()
    {
        Id = "call-1",
        Name = toolName,
        Arguments = JsonDocument.Parse("{}").RootElement.Clone()
    };
}
