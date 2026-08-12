using System.IO;
using InsightaAI.Agent.Cli.UI;
using InsightaAI.Agent.Models;
using Spectre.Console;

namespace InsightaAI.Agent.Tests;

public sealed class EventRendererTests
{
    [Fact]
    public async Task ToolStart_Should_Not_Render_Until_Its_ToolEnd()
    {
        var output = new StringWriter();
        using var renderer = new EventRenderer(CreateConsole(output));

        await renderer.HandleEventAsync(ToolStart("first", "web_search", "{\"query\":\"test\"}"));

        Assert.Equal(string.Empty, output.ToString());

        await renderer.HandleEventAsync(ToolEnd("first", "web_search", "search result"));

        Assert.Contains("○ web_search", output.ToString());
        Assert.Contains("⎿ search result", output.ToString());
    }

    [Fact]
    public async Task ParallelTools_Should_Render_Each_Result_With_Its_Own_Call_In_Completion_Order()
    {
        var output = new StringWriter();
        using var renderer = new EventRenderer(CreateConsole(output));

        await renderer.HandleEventAsync(ToolStart("search", "web_search", "{\"query\":\"one\"}"));
        await renderer.HandleEventAsync(ToolStart("fetch", "web_fetch", "{\"url\":\"https://example.com\"}"));
        await renderer.HandleEventAsync(ToolEnd("fetch", "web_fetch", "fetch result"));
        await renderer.HandleEventAsync(ToolEnd("search", "web_search", "search result"));

        var rendered = output.ToString();
        var fetchCall = rendered.IndexOf("○ web_fetch", StringComparison.Ordinal);
        var fetchResult = rendered.IndexOf("⎿ fetch result", StringComparison.Ordinal);
        var searchCall = rendered.IndexOf("○ web_search", StringComparison.Ordinal);
        var searchResult = rendered.IndexOf("⎿ search result", StringComparison.Ordinal);

        Assert.True(fetchCall < fetchResult && fetchResult < searchCall && searchCall < searchResult);
    }

    [Fact]
    public async Task FailedTool_Should_Render_Error_Result_With_The_Matching_Call()
    {
        var output = new StringWriter();
        using var renderer = new EventRenderer(CreateConsole(output));

        await renderer.HandleEventAsync(ToolStart("failed", "web_fetch", "{\"url\":\"https://example.com\"}"));
        await renderer.HandleEventAsync(new AgentToolEndEvent
        {
            AgentId = "test-agent",
            ToolCallId = "failed",
            ToolName = "web_fetch",
            IsError = true,
            ResultPreview = "request failed"
        });

        var rendered = output.ToString();
        Assert.True(rendered.IndexOf("○ web_fetch", StringComparison.Ordinal) < rendered.IndexOf("⎿ request failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ToolEnd_Without_A_ToolStart_Should_Render_A_Fallback_Call()
    {
        var output = new StringWriter();
        using var renderer = new EventRenderer(CreateConsole(output));

        await renderer.HandleEventAsync(ToolEnd("missing", "web_search", "search result"));

        Assert.Contains("○ web_search", output.ToString());
        Assert.Contains("⎿ search result", output.ToString());
    }

    private static IAnsiConsole CreateConsole(StringWriter output) =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(output)
        });

    private static AgentToolStartEvent ToolStart(string id, string name, string arguments) => new()
    {
        AgentId = "test-agent",
        ToolCallId = id,
        ToolName = name,
        Arguments = arguments
    };

    private static AgentToolEndEvent ToolEnd(string id, string name, string result) => new()
    {
        AgentId = "test-agent",
        ToolCallId = id,
        ToolName = name,
        ResultPreview = result
    };
}
