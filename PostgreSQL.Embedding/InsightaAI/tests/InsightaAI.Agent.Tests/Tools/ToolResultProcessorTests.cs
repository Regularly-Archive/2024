using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Harness.Local;
using InsightaAI.Agent.Tools;
using InsightaAI.Agent.Tools.BuiltIn;
using InsightaAI.Agent.Storage;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tests.Tools;

public sealed class ToolResultProcessorTests
{
    [Fact]
    public async Task ProcessAsync_Should_PersistOriginalContentBeforeCreatingPreview()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"insighta-test-{Guid.NewGuid():N}");
        try
        {
            var original = string.Join("\n", Enumerable.Range(1, 2000).Select(i => $"line {i}: {new string('x', 32)}"));
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
                "session-1", "custom_tool", "call-1", result, enabled: true);

            Assert.Equal(ToolResultRetentionLevel.Preview, processed.State.RetentionLevel);
            Assert.NotNull(processed.State.Artifact);
            Assert.True(processed.Result.IsError);
            Assert.Same(metadata, processed.Result.Metadata);
            Assert.Equal(original, await File.ReadAllTextAsync(processed.State.Artifact.Path));
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
            "session-1", "custom_tool", "call-1", ToolResult.FromText("small result"), enabled: true);

        Assert.Equal(ToolResultRetentionLevel.Full, processed.State.RetentionLevel);
        Assert.Null(processed.State.Artifact);
        Assert.Equal("small result", processed.Result.Content.OfType<TextBlock>().Single().Text);
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
}
