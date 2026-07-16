using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace InsightaAI.Agent.Diagnostics;

/// <summary>
/// 集中管理 ActivitySource 和 Meter 实例
/// </summary>
internal static class TelemetryConstants
{
    public const string ActivitySourceName = "InsightaAI.Agent";
    public const string MeterName = "InsightaAI.Agent";

    private static string Version { get; } =
        typeof(TelemetryConstants).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);
    public static readonly Meter Meter = new(MeterName, Version);

    /// <summary>
    /// 通过 agentId 存储当前 round 的 Activity。
    /// IAsyncEnumerable yield 边界会丢失 Activity.Current 和 AsyncLocal，
    /// 改用静态字典 + decorator 构造时持有的 agentId 跨上下文传递。
    /// </summary>
    internal static readonly ConcurrentDictionary<string, ActivityContext> CurrentRoundContext = new();

    // Counter instruments
    public static readonly Counter<long> ClientInputTokenCounter =
        Meter.CreateCounter<long>("gen_ai.client.tokens.input", "tokens", "Input/prompt tokens consumed");
    public static readonly Counter<long> ClientOutputTokenCounter =
        Meter.CreateCounter<long>("gen_ai.client.tokens.output", "tokens", "Output/completion tokens consumed");
    public static readonly Counter<long> ClientCacheHitTokenCounter =
        Meter.CreateCounter<long>("gen_ai.client.tokens.cache_hit", "tokens", "Cache hit tokens");
    public static readonly Counter<long> AgentRunCounter =
        Meter.CreateCounter<long>("insighta.agent.run.total", "runs", "Total agent runs");

    // Histogram instruments
    public static readonly Histogram<double> ClientOperationDuration =
        Meter.CreateHistogram<double>("gen_ai.client.operation.duration", "ms", "LLM request duration");
    public static readonly Histogram<double> ToolExecutionDuration =
        Meter.CreateHistogram<double>("insighta.tool.execution.duration", "ms", "Tool execution duration");
    public static readonly Histogram<double> AgentRoundDuration =
        Meter.CreateHistogram<double>("insighta.agent.round.duration", "ms", "Agent round duration");
}
