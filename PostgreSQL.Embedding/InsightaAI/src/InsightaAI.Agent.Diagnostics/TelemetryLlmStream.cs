using System.Diagnostics;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Diagnostics;

/// <summary>
/// LlmStream 装饰器 — 在流完成时记录 token 用量和延迟
/// </summary>
public sealed class TelemetryLlmStream : LlmStream
{
    private readonly LlmStream _inner;
    private Activity? _activity;
    private readonly string _provider;
    private readonly string _model;
    private readonly Stopwatch _stopwatch;
    private bool _metricsRecorded;
    private bool _disposed;

    public TelemetryLlmStream(LlmStream inner, Activity? activity, string provider, string model)
    {
        _inner = inner;
        _activity = activity;
        _provider = provider;
        _model = model;
        _stopwatch = Stopwatch.StartNew();
    }

    public bool IsCompleted => _inner.IsCompleted;
    public bool IsAborted => _inner.IsAborted;

    public async IAsyncEnumerator<StreamEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        // 注意：C# 不允许在带 catch 的 try 块中 yield，因此这里只用 finally。
        // 异常会自动向上传播，Activity 的 RecordException 由调用方或 OTel 自动处理。
        try
        {
            await foreach (var evt in _inner.WithCancellation(cancellationToken))
            {
                yield return evt;
            }
        }
        finally
        {
            // 流遍历结束后仅停止计时，不关闭 Activity。
            // GetResponseAsync 还需要 Activity 来记录 token usage。
            // 如果 GetResponseAsync 未被调用，Dispose() 会兜底清理。
            if (_stopwatch.IsRunning)
            {
                _stopwatch.Stop();
            }
        }
    }

    public async Task<LlmResponse> GetResponseAsync(CancellationToken cancellationToken = default)
    {
        var response = await _inner.GetResponseAsync(cancellationToken);
        if (!_stopwatch.IsRunning) _stopwatch.Stop();

        TelemetryLlmClient.RecordMetricsAndTags(
            _activity, response.Usage, _stopwatch.ElapsedMilliseconds, null);
        FinishActivity();

        return response;
    }

    public void Abort() => _inner.Abort();

    /// <summary>
    /// 关闭 Activity 并置 null，防止双重 Dispose
    /// </summary>
    private void FinishActivity()
    {
        _metricsRecorded = true;
        _activity?.Dispose();
        _activity = null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _inner.Dispose();

            // 如果 GetResponseAsync 未被调用（仅使用了 GetAsyncEnumerator），
            // 此处兜底记录 duration-only metrics 并关闭 Activity
            if (!_metricsRecorded && _activity != null)
            {
                if (_stopwatch.IsRunning) _stopwatch.Stop();
                TelemetryLlmClient.RecordMetricsAndTags(
                    _activity, null, _stopwatch.ElapsedMilliseconds, null);
            }
            _activity?.Dispose();
            _activity = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
