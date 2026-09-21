using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Tools.BuiltIn;
using InsightaAI.Agent.Vision;
using InsightaAI.LLM.Models;
using Microsoft.Extensions.DependencyInjection;

namespace InsightaAI.Agent.Tests.Tools;

public sealed class VisionToolTests
{
    private sealed class FakeVisionService : IVisionService
    {
        public string? Source { get; private set; }

        public string? Prompt { get; private set; }

        /// <summary>自定义描述生成器（入参为 source）；null 时返回固定描述。</summary>
        public Func<string, string>? Handler { get; init; }

        public Task<string> AnalyzeAsync(string source, string prompt, CancellationToken cancellationToken = default)
        {
            Source = source;
            Prompt = prompt;
            return Task.FromResult(Handler?.Invoke(source) ?? "a red apple on a wooden table");
        }
    }

    private static ToolExecutionContext CreateContext(IServiceProvider? services = null) =>
        new() { AgentId = "agent", ToolCallId = "call-1", Services = services };

    private static string GetText(ToolResult result) =>
        Assert.IsType<TextBlock>(Assert.Single(result.Content)).Text;

    [Fact]
    public async Task ExecuteAsync_MissingSource_ReturnsError()
    {
        var tool = new VisionTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object>(), CreateContext());

        Assert.True(result.IsError);
        Assert.Contains("source", GetText(result));
    }

    [Fact]
    public async Task ExecuteAsync_ServiceNotRegistered_ReturnsConfigError()
    {
        var tool = new VisionTool();

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object> { ["source"] = "cat.png" },
            CreateContext(services: null));

        Assert.True(result.IsError);
        Assert.Contains("not configured", GetText(result));
    }

    [Fact]
    public async Task ExecuteAsync_Success_ReturnsDescriptionAndPassesArgs()
    {
        var fake = new FakeVisionService();
        var services = new ServiceCollection().AddSingleton<IVisionService>(fake).BuildServiceProvider();
        var tool = new VisionTool();

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object> { ["source"] = "cat.png" },
            CreateContext(services));

        Assert.False(result.IsError);
        Assert.Equal("a red apple on a wooden table", GetText(result));
        Assert.Equal("cat.png", fake.Source);
        Assert.Equal("请用中文尽可能详细地描述这张图片。", fake.Prompt); // 默认 prompt
    }

    [Fact]
    public async Task ExecuteAsync_CustomPrompt_PassedThrough()
    {
        var fake = new FakeVisionService();
        var services = new ServiceCollection().AddSingleton<IVisionService>(fake).BuildServiceProvider();
        var tool = new VisionTool();

        await tool.ExecuteAsync(
            new Dictionary<string, object> { ["source"] = "chart.png", ["prompt"] = "Summarize the trends." },
            CreateContext(services));

        Assert.Equal("Summarize the trends.", fake.Prompt);
    }

    [Fact]
    public async Task ExecuteAsync_BlankPrompt_FallsBackToDefault()
    {
        var fake = new FakeVisionService();
        var services = new ServiceCollection().AddSingleton<IVisionService>(fake).BuildServiceProvider();
        var tool = new VisionTool();

        await tool.ExecuteAsync(
            new Dictionary<string, object> { ["source"] = "cat.png", ["prompt"] = "   " },
            CreateContext(services));

        Assert.Equal("请用中文尽可能详细地描述这张图片。", fake.Prompt);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyDescription_ReturnsError()
    {
        var fake = new FakeVisionService { Handler = _ => "   " };
        var services = new ServiceCollection().AddSingleton<IVisionService>(fake).BuildServiceProvider();
        var tool = new VisionTool();

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object> { ["source"] = "cat.png" },
            CreateContext(services));

        Assert.True(result.IsError);
        Assert.Contains("empty", GetText(result));
    }

    [Fact]
    public async Task ExecuteAsync_ServiceThrows_ConvertsToErrorWithSource()
    {
        var fake = new FakeVisionService
        {
            Handler = _ => throw new InvalidOperationException("boom")
        };
        var services = new ServiceCollection().AddSingleton<IVisionService>(fake).BuildServiceProvider();
        var tool = new VisionTool();

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object> { ["source"] = "cat.png" },
            CreateContext(services));

        Assert.True(result.IsError);
        var text = GetText(result);
        Assert.Contains("cat.png", text);
        Assert.Contains("boom", text);
    }
}
