using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Harness.Local;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Tools.BuiltIn;

namespace InsightaAI.Agent.Tests.Tools;

public sealed class FileEditToolTests
{
    [Theory]
    [InlineData("[REDACTED]", "replacement")]
    [InlineData("original", "[REDACTED]")]
    public async Task ExecuteAsync_ShouldRejectRedactionPlaceholder(string oldString, string newString)
    {
        var tool = new FileEditTool(new LocalFileSystem(), new LocalPathValidator(), new FileReadState());

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object>
            {
                ["file_path"] = Path.Combine(Path.GetTempPath(), $"insighta-redaction-{Guid.NewGuid():N}.txt"),
                ["old_string"] = oldString,
                ["new_string"] = newString
            },
            new ToolExecutionContext { AgentId = "test", ToolCallId = "call-1" });

        Assert.True(result.IsError);
        Assert.Contains("secret-redaction placeholder", result.Content.OfType<InsightaAI.LLM.Models.TextBlock>().Single().Text);
    }
}
