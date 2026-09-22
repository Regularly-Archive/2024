using System.Net;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Tools.BuiltIn;
using Microsoft.Extensions.DependencyInjection;

namespace InsightaAI.Agent.Tests.Tools;

public sealed class WebSearchToolTests
{
    [Fact]
    public async Task ExecuteAsync_ReportsStatusAndFoundPages()
    {
        var handler = new StubTavilyHandler("""
            {
              "answer": "synthesized answer",
              "results": [
                { "title": "First Result", "url": "https://example.com/first", "content": "a" },
                { "title": "Second Result", "url": "https://example.com/second", "content": "b" }
              ]
            }
            """);
        var reporter = new RecordingProgressReporter();
        var tool = new WebSearchTool(new HttpClient(handler));
        var args = new Dictionary<string, object> { ["query"] = "insighta progress" };

        var result = await tool.ExecuteAsync(args, CreateContext(reporter));

        Assert.False(result.IsError);
        Assert.Equal(3, reporter.Updates.Count);

        var status = reporter.Updates[0];
        Assert.Equal(ToolProgressKind.Status, status.Kind);
        Assert.Contains("insighta progress", status.Message);

        var first = reporter.Updates[1];
        Assert.Equal(ToolProgressKind.Output, first.Kind);
        Assert.Contains("1. First Result", first.Text);
        Assert.Contains("https://example.com/first", first.Text);

        var second = reporter.Updates[2];
        Assert.Equal(ToolProgressKind.Output, second.Kind);
        Assert.Contains("2. Second Result", second.Text);
        Assert.Contains("https://example.com/second", second.Text);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyResults_ReportsOnlyStatus()
    {
        var handler = new StubTavilyHandler("""{ "answer": null, "results": [] }""");
        var reporter = new RecordingProgressReporter();
        var tool = new WebSearchTool(new HttpClient(handler));
        var args = new Dictionary<string, object> { ["query"] = "empty" };

        var result = await tool.ExecuteAsync(args, CreateContext(reporter));

        Assert.False(result.IsError);
        var update = Assert.Single(reporter.Updates);
        Assert.Equal(ToolProgressKind.Status, update.Kind);
    }

    [Fact]
    public async Task ExecuteAsync_MissingApiKey_ReportsNothing()
    {
        var reporter = new RecordingProgressReporter();
        var tool = new WebSearchTool(new HttpClient(new StubTavilyHandler("{}")));
        var args = new Dictionary<string, object> { ["query"] = "anything" };

        var result = await tool.ExecuteAsync(args, CreateContext(reporter, envKey: null));

        Assert.True(result.IsError);
        Assert.Empty(reporter.Updates);
    }

    private static ToolExecutionContext CreateContext(
        IToolProgressReporter reporter,
        string? envKey = "test-api-key")
    {
        var services = new ServiceCollection()
            .AddSingleton<IEnvironmentVariableReader>(new StubEnvReader(envKey))
            .BuildServiceProvider();
        return new ToolExecutionContext
        {
            AgentId = "agent-1",
            ToolCallId = "call-1",
            Services = services,
            Progress = reporter
        };
    }

    private sealed class StubEnvReader(string? value) : IEnvironmentVariableReader
    {
        public string? Get(string name) => name == "TAVILY_API_KEY" ? value : null;
    }

    private sealed class StubTavilyHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingProgressReporter : IToolProgressReporter
    {
        public List<ToolProgressUpdate> Updates { get; } = [];

        public ValueTask ReportAsync(ToolProgressUpdate update, CancellationToken cancellationToken = default)
        {
            Updates.Add(update);
            return ValueTask.CompletedTask;
        }
    }
}
