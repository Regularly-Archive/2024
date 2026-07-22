using System.Diagnostics;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Diagnostics;

/// <summary>
/// IAgentEventHook 实现 — 在 turn/round 级别创建 OpenTelemetry span 并记录 metrics
/// </summary>
/// <remarks>
/// 假设单线程顺序调用（Agent 按 round 顺序依次调用 hook 方法）。
/// 如果未来支持并发 round，需要对 _turnActivityContext / _roundActivity 加锁。
/// </remarks>
public sealed class AgentEventTelemetryHook : IAgentEventHook
{
    private ActivityContext _turnActivityContext;
    private Activity? _roundActivity;
    private ActivityContext _roundActivityContext;
    private Stopwatch? _roundStopwatch;
    private int _currentRound;

    private string? _agentId;
    private string? _agentName;
    private string? _model;
    private string? _sessionId;

    public string Id => "opentelemetry";

    /// <summary>
    /// 设置会话上下文（在第一次 OnRoundStartAsync 之前调用）
    /// </summary>
    public void SetSessionContext(string agentId, string agentName, string model, string sessionId)
    {
        _agentId = agentId;
        _agentName = agentName;
        _model = model;
        _sessionId = sessionId;
    }

    public Task OnAgentTurnStartedAsync(AgentEventHookContext context, string message, CancellationToken cancellationToken = default)
    {
        // 创建 turn span
        using var turnActivity = TelemetryConstants.ActivitySource.StartActivity(
            "insighta.agent.turn_start", ActivityKind.Internal);

        if (turnActivity != null)
        {
            turnActivity.SetTag("agent.id", _agentId);
            turnActivity.SetTag("agent.name", _agentName);
            turnActivity.SetTag("gen_ai.request.model", _model);
            turnActivity.SetTag("session.id", _sessionId);
            _turnActivityContext = turnActivity.Context;
        }

        _currentRound = 0;

        TelemetryConstants.AgentRunCounter.Add(1,
        [
            new KeyValuePair<string, object?>("agent.id", _agentId),
            new KeyValuePair<string, object?>("gen_ai.request.model", _model)
        ]);

        return Task.CompletedTask;
    }

    public Task OnAgentRoundStartedAsync(string message, CancellationToken cancellationToken = default)
    {
        // 结束上一轮的 span（多轮场景）
        EndRoundActivity();

        _currentRound++;
        _roundActivity = TelemetryConstants.ActivitySource.StartActivity(
            "insighta.agent.round", ActivityKind.Internal, parentContext: _turnActivityContext);

        if (_roundActivity != null)
        {
            _roundActivity.SetTag("agent.id", _agentId);
            _roundActivity.SetTag("round.number", _currentRound);
            _roundActivityContext = _roundActivity.Context;
        }

        // 通过静态字典传递 round Activity，绕过 IAsyncEnumerable yield 边界丢失 Activity.Current 的问题
        if (_agentId != null && _roundActivity != null)
        {
            TelemetryConstants.CurrentRoundContext[_agentId] = _roundActivity.Context;
        }

        _roundStopwatch = Stopwatch.StartNew();
        return Task.CompletedTask;
    }

    public Task OnAgentTurnEndedAsync(
        AgentEventHookContext context,
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken = default)
    {
        EndRoundActivity();

        using var turnActivity = TelemetryConstants.ActivitySource.StartActivity(
            "insighta.agent.turn_end", ActivityKind.Internal, parentContext: _turnActivityContext);

        turnActivity.SetTag("session.id", _sessionId);
        turnActivity.SetTag("turn.total_rounds", _currentRound);

        var turnEndEvt = context.Event as AgentTurnEndEvent;
        turnActivity.SetTag("turn.duration_ms", turnEndEvt.Result.DurationMs);

        turnActivity.SetStatus(ActivityStatusCode.Ok);

        if (_agentId != null)
        {
            TelemetryConstants.CurrentRoundContext.TryRemove(_agentId, out _);
        }

        return Task.CompletedTask;
    }

    private void EndRoundActivity()
    {
        if (_roundActivity != null)
        {
            _roundStopwatch?.Stop();
            var durationMs = _roundStopwatch?.ElapsedMilliseconds ?? 0;

            _roundActivity.SetTag("round.duration_ms", durationMs);
            _roundActivity.SetStatus(ActivityStatusCode.Ok);
            _roundActivity.Dispose();
            _roundActivity = null;
            _roundActivityContext = default;

            TelemetryConstants.AgentRoundDuration.Record(durationMs,
            [
                new KeyValuePair<string, object?>("agent.id", _agentId),
                new KeyValuePair<string, object?>("round.number", _currentRound)
            ]);
        }
    }
}
