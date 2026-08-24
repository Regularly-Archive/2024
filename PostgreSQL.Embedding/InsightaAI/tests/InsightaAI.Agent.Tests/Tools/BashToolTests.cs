using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Tools.BuiltIn;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tests.Tools;

public sealed class BashToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldKeepLargeOutputForResultProcessor()
    {
        var output = new string('x', 10_001);
        var tool = new BashTool(new StubShellExecutor(new ShellResult { Stdout = output }));

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object> { ["command"] = "test" }, CreateContext());

        var text = result.Content.OfType<TextBlock>().Single().Text;
        Assert.Contains(output, text);
        Assert.DoesNotContain("Command output is too long", text);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPropagateCancellation()
    {
        var tool = new BashTool(new StubShellExecutor(new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(() => tool.ExecuteAsync(
            new Dictionary<string, object> { ["command"] = "test" }, CreateContext()));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportStreamingOutputWhenSupported()
    {
        var progress = new RecordingProgressReporter();
        var tool = new BashTool(new StreamingShellExecutor(new ShellResult
        {
            Stdout = "stdout line",
            Stderr = "stderr line"
        }));

        await tool.ExecuteAsync(
            new Dictionary<string, object> { ["command"] = "test" },
            CreateContext() with { Progress = progress });

        Assert.Contains(progress.Updates, update =>
            update.Kind == ToolProgressKind.Status && update.Message == "Shell command started.");
        Assert.Contains(progress.Updates, update =>
            update.Kind == ToolProgressKind.Output &&
            update.Stream == ToolOutputStream.Stdout && update.Text == "stdout line");
        Assert.Contains(progress.Updates, update =>
            update.Kind == ToolProgressKind.Output &&
            update.Stream == ToolOutputStream.Stderr && update.Text == "stderr line");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectArgumentsNotDeclaredBySchema()
    {
        var tool = new BashTool(new StubShellExecutor(new ShellResult()));

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object> { ["command"] = "test", ["unexpected"] = true }, CreateContext());

        var text = result.Content.OfType<TextBlock>().Single().Text;
        Assert.Contains("'unexpected' is not declared in the tool schema", text);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectArgumentsWithTheWrongSchemaType()
    {
        var tool = new BashTool(new StubShellExecutor(new ShellResult()));

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object> { ["command"] = "test", ["working_directory"] = 42 }, CreateContext());

        var text = result.Content.OfType<TextBlock>().Single().Text;
        Assert.Contains("'working_directory' must be a string", text);
    }

    [Fact]
    public void CreatePreview_ShouldShowHeadAndTailForLargeLineOutput()
    {
        var tool = new BashTool(new StubShellExecutor(new ShellResult()));
        var text = string.Join("\n", Enumerable.Range(1, 101));
        var result = ToolResult.FromText(text);

        var preview = tool.CreatePreview(result, new ToolResultProjectionContext
        {
            ToolName = "bash",
            ToolCallId = "call-1",
            OriginalLength = text.Length,
            OriginalLineCount = new Lazy<int>(() => 101)
        });

        var previewText = preview.Content.OfType<TextBlock>().Single().Text;
        Assert.Contains("1", previewText);
        Assert.Contains("101", previewText);
        Assert.Contains("omitted 1 lines", previewText);
    }

    private static ToolExecutionContext CreateContext() => new()
    {
        AgentId = "agent-1",
        ToolCallId = "call-1"
    };

    private sealed class StubShellExecutor : IShellExecutor
    {
        private readonly ShellResult? _result;
        private readonly Exception? _exception;

        public StubShellExecutor(ShellResult result) => _result = result;
        public StubShellExecutor(Exception exception) => _exception = exception;

        public Task<ShellResult> ExecuteAsync(
            string command, string? workingDirectory = null, CancellationToken cancellationToken = default)
        {
            if (_exception != null)
                return Task.FromException<ShellResult>(_exception);
            return Task.FromResult(_result!);
        }
    }

    private sealed class StreamingShellExecutor(ShellResult result) : IStreamingShellExecutor
    {
        public Task<ShellResult> ExecuteAsync(
            string command, string? workingDirectory = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);

        public async Task<ShellResult> ExecuteStreamingAsync(
            string command,
            string? workingDirectory,
            Func<ToolOutputStream, string, CancellationToken, ValueTask> onOutput,
            CancellationToken cancellationToken = default)
        {
            await onOutput(ToolOutputStream.Stdout, result.Stdout, cancellationToken);
            await onOutput(ToolOutputStream.Stderr, result.Stderr, cancellationToken);
            return result;
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
