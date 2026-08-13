using InsightaAI.Agent.Tools;
using System.Diagnostics;
using System.Text.Json;

namespace InsightaAI.Agent.Diagnostics;

/// <summary>
/// 包装 ToolCallHandler 委托 — 为每次工具执行添加 OpenTelemetry span 和 metrics
/// </summary>
public static class ToolCallHandlerTelemetryWrapper
{
    /// <summary>
    /// 用 telemetry 包装给定的 handler
    /// </summary>
    public static ToolCallHandler Wrap(ToolCallHandler inner, string? agentId = null)
    {
        return async (request, cancellationToken) =>
        {
            // 强制从字典恢复 round Activity，确保 tool_call 正确挂在 round 下
            // IAsyncEnumerable yield 边界会导致 Activity.Current 丢失或指向错误的 Activity
            // 字典缺失时降级为无父 context（如测试环境未挂 round hook）
            ActivityContext activityContext = default;
            if (agentId != null && TelemetryConstants.CurrentRoundContext.TryGetValue(agentId, out var roundCtx))
            {
                activityContext = roundCtx;
            }

            using var activity = TelemetryConstants.ActivitySource.StartActivity(
                "insighta.agent.tool_call", ActivityKind.Internal, parentContext: activityContext);

            if (activity != null)
            {
                activity.SetTag("gen_ai.agent.name", agentId);
                activity.SetTag("gen_ai.agent.description", string.Empty);
                activity.SetTag("gen_ai.tool.name", request.ToolCall.Name);
                activity.SetTag("gen_ai.tool.call_id", request.ToolCall.Id);
                activity.SetTag("gen_ai.tool.arguments", request.ToolCall.Arguments.ToString());
            }

            var sw = Stopwatch.StartNew();
            try
            {
                var response = await inner(request, cancellationToken);
                sw.Stop();

                if (activity != null)
                {
                    var isError = !response.IsAllowed || response.ToolResult.IsError;
                    activity.SetTag("gen_ai.tool.is_error", isError);
                    activity.SetTag("gen_ai.tool.duration_ms", sw.ElapsedMilliseconds);

                    // 消费工具执行层产出的元数据（如 MCP server 信息等）
                    if (response.ToolResult.Metadata is { } meta)
                    {
                        foreach (var kv in meta)
                            activity.SetTag(kv.Key, kv.Value);
                    }

                    activity.SetStatus(isError ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
                }

                TelemetryConstants.ToolExecutionDuration.Record(sw.ElapsedMilliseconds,
                [
                    new KeyValuePair<string, object?>("gen_ai.tool.name", request.ToolCall.Name),
                    new KeyValuePair<string, object?>("gen_ai.tool.is_error", response.ToolResult.IsError),
                    new KeyValuePair<string, object?>("gen_ai.tool.is_allowed", response.IsAllowed)
                ]);

                if (request.ToolCall.Name == "activate_skill" &&
                    response.IsAllowed &&
                    !response.ToolResult.IsError &&
                    request.ToolCall.Arguments.ValueKind == JsonValueKind.Object &&
                    request.ToolCall.Arguments.TryGetProperty("skill_name", out var skillNameElement) &&
                    skillNameElement.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(skillNameElement.GetString()))
                {
                    TelemetryConstants.SkillActivationCounter.Add(1,
                    [
                        new KeyValuePair<string, object?>("insighta.skill.name", skillNameElement.GetString())
                    ]);
                }

                return response;
            }
            catch (Exception ex)
            {
                sw.Stop();

                if (activity != null)
                {
                    activity.SetTag("gen_ai.tool.duration_ms", sw.ElapsedMilliseconds);
                    activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                    activity.SetTag("gen_ai.tool.error.type", ex.GetType().Name);
                }

                TelemetryConstants.ToolExecutionDuration.Record(sw.ElapsedMilliseconds,
                [
                    new KeyValuePair<string, object?>("gen_ai.tool.name", request.ToolCall.Name),
                    new KeyValuePair<string, object?>("gen_ai.tool.is_error", true),
                    new KeyValuePair<string, object?>("gen_ai.tool.is_allowed", true)
                ]);

                throw;
            }
        };
    }
}
