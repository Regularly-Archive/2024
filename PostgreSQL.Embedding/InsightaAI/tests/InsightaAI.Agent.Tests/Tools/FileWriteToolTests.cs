using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Harness.Local;
using InsightaAI.Agent.Tools.BuiltIn;

namespace InsightaAI.Agent.Tests.Tools;

public sealed class FileWriteToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldUseInjectedPathValidatorBeforeWriting()
    {
        var path = Path.Combine(Path.GetTempPath(), $"insighta-path-validator-{Guid.NewGuid():N}.txt");
        var tool = new FileWriteTool(new LocalFileSystem(), new RejectingPathValidator());

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object>
            {
                ["file_path"] = path,
                ["content"] = "must not be written"
            },
            new ToolExecutionContext { AgentId = "test", ToolCallId = "call-1" });

        Assert.True(result.IsError);
        Assert.Contains("blocked by policy", result.Content.OfType<InsightaAI.LLM.Models.TextBlock>().Single().Text);
        Assert.False(File.Exists(path));
    }

    private sealed class RejectingPathValidator : IPathValidator
    {
        public PathValidationResult Validate(string path, string? workingDirectory = null) =>
            PathValidationResult.Dangerous("blocked by policy");
    }
}
