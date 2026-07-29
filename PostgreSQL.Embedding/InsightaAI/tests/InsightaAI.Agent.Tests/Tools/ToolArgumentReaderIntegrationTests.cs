using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Tools;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tests.Tools;

public sealed class ToolArgumentReaderIntegrationTests
{
    [Fact]
    public async Task AskUserTool_ShouldAllowOmittedChoicesDefinedAsOptional()
    {
        string[]? receivedChoices = ["sentinel"];
        var tool = new AskUserTool((_, choices, _) =>
        {
            receivedChoices = choices;
            return Task.FromResult("answer");
        });

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object> { ["question"] = "Continue?" }, CreateContext());

        Assert.False(result.IsError);
        Assert.Null(receivedChoices);
    }

    [Fact]
    public async Task AskUserTool_ShouldAcceptJsonConvertedArrayArguments()
    {
        string[]? receivedChoices = null;
        var tool = new AskUserTool((_, choices, _) =>
        {
            receivedChoices = choices;
            return Task.FromResult("answer");
        });

        await tool.ExecuteAsync(
            new Dictionary<string, object> { ["question"] = "Choose", ["choices"] = new object[] { "A", "B" } }, CreateContext());

        Assert.Equal(["A", "B"], Assert.IsType<string[]>(receivedChoices));
    }

    [Fact]
    public async Task TerminateTool_ShouldRejectMissingSchemaRequiredArgument()
    {
        var result = await new TerminateTool().ExecuteAsync(new Dictionary<string, object>(), CreateContext());

        var text = result.Content.OfType<TextBlock>().Single().Text;
        Assert.Contains("Missing required parameter: answer", text);
    }

    private static ToolExecutionContext CreateContext() => new()
    {
        AgentId = "agent-1",
        ToolCallId = "call-1"
    };
}
