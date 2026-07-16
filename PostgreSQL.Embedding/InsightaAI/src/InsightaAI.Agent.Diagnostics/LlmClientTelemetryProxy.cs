using System.Diagnostics;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Diagnostics;

/// <summary>
/// ILlmClient 装饰器 — 为每次 LLM 调用添加 OpenTelemetry span 和 metrics
/// </summary>
public sealed class LlmClientTelemetryProxy : ILlmClient
{
    private readonly ILlmClient _inner;
    private readonly string? _agentId;

    public LlmClientTelemetryProxy(ILlmClient inner, string? agentId = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _agentId = agentId;
    }

    public string AdapterName => _inner.AdapterName;
    public bool SupportsReasoning => _inner.SupportsReasoning;

    public LlmStream Streaming(LlmRequest request)
    {
        var activity = StartChildActivity("insighta.llm.request");

        if (activity != null)
        {
            activity.SetTag("gen_ai.system", _inner.AdapterName);
            activity.SetTag("gen_ai.adapter", _inner.AdapterName);
            activity.SetTag("gen_ai.request.model", request.Model);
            activity.SetTag("gen_ai.request.is_stream", true);
        }

        var innerStream = _inner.Streaming(request);
        return new LlmStreamTelemetryProxy(innerStream, activity, _inner.AdapterName, request.Model);
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = StartChildActivity("insighta.llm.request");

        if (activity != null)
        {
            activity.SetTag("gen_ai.system", _inner.AdapterName);
            activity.SetTag("gen_ai.adapter", _inner.AdapterName);
            activity.SetTag("gen_ai.request.model", request.Model);
            activity.SetTag("gen_ai.request.is_stream", false);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await _inner.CompleteAsync(request, cancellationToken);
            sw.Stop();

            RecordMetricsAndTags(activity, response.Usage, sw.ElapsedMilliseconds, null);
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordMetricsAndTags(activity, null, sw.ElapsedMilliseconds, ex);
            throw;
        }
    }

    internal static void RecordMetricsAndTags(Activity? activity, TokenUsage? usage, long durationMs, Exception? error)
    {
        var adapterTag = activity?.GetTagItem("gen_ai.adapter")?.ToString() ?? "unknown";
        var systemTag = activity?.GetTagItem("gen_ai.system")?.ToString() ?? "unknown";
        var modelTag = activity?.GetTagItem("gen_ai.request.model")?.ToString() ?? "unknown";

        var tags = new TagList
        {
            { "gen_ai.adapter", adapterTag },
            { "gen_ai.system", systemTag },
            { "gen_ai.request.model", modelTag }
        };

        TelemetryConstants.LlmRequestDuration.Record(durationMs, tags);

        if (usage != null)
        {
            TelemetryConstants.InputTokenCounter.Add(usage.InputTokens, tags);
            TelemetryConstants.OutputTokenCounter.Add(usage.OutputTokens, tags);
            TelemetryConstants.CacheHitTokenCounter.Add(usage.CacheHitTokens, tags);

            activity?.SetTag("gen_ai.usage.input_tokens", usage.InputTokens);
            activity?.SetTag("gen_ai.usage.output_tokens", usage.OutputTokens);
            activity?.SetTag("gen_ai.usage.cache_hit_tokens", usage.CacheHitTokens);
        }

        activity?.SetTag("gen_ai.client.operation.duration", durationMs);

        if (error != null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, error.Message);
            activity?.SetTag("error.type", error.GetType().Name);
            activity?.SetTag("error.message", error.Message);
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
    }

    /// <summary>
    /// 创建子 Activity，强制从静态字典恢复 round Activity。
    /// IAsyncEnumerable yield 边界会导致 Activity.Current 丢失或指向错误的 Activity，
    /// 通过构造时传入的 agentId 从字典查找，确保正确挂在 round span 下。
    /// </summary>
    private Activity? StartChildActivity(string name)
    {
        // 强制从字典恢复 round Activity，确保正确挂在 round 下
        ActivityContext roundActivityContext = default;
        if (_agentId != null)
        {
            roundActivityContext = TelemetryConstants.CurrentRoundContext[_agentId];
        }

        return TelemetryConstants.ActivitySource.StartActivity(name, ActivityKind.Client, parentContext: roundActivityContext);
    }

    public void Dispose()
    {
        if (_inner is IDisposable d) d.Dispose();
        GC.SuppressFinalize(this);
    }
}
