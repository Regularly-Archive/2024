using System.Text.Json;
using System.Text.Json.Nodes;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Cli.Run;

/// <summary>
/// Writes the stable stdout contract for <c>insighta run</c>. One JSON object per line; no
/// human-facing output is written to stdout, so piped consumers can parse it safely.
/// </summary>
public sealed class RunJsonlWriter(TextWriter output)
{
    public const int ProtocolVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private int _sequence;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async ValueTask RunStartedAsync(
        string sessionId,
        string model,
        string? profileId,
        CancellationToken cancellationToken = default)
    {
        var envelope = CreateEnvelope("run.started");
        envelope["sessionId"] = sessionId;
        envelope["model"] = model;
        envelope["profileId"] = profileId;
        await WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask AgentEventAsync(
        AgentEvent agentEvent,
        CancellationToken cancellationToken = default)
    {
        var envelope = CreateEnvelope("agent.event");
        envelope["event"] = JsonSerializer.SerializeToNode(agentEvent, JsonOptions);
        await WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RunCompletedAsync(
        string? sessionId,
        AgentResult? result,
        int exitCode,
        CancellationToken cancellationToken = default)
    {
        var type = result?.Status switch
        {
            AgentStatus.Completed => "run.completed",
            AgentStatus.Aborted => "run.cancelled",
            _ => "run.failed"
        };
        var envelope = CreateEnvelope(type);
        envelope["sessionId"] = sessionId;
        envelope["exitCode"] = exitCode;

        if (result is not null)
        {
            envelope["status"] = result.Status.ToString().ToLowerInvariant();
            envelope["result"] = JsonSerializer.SerializeToNode(result, JsonOptions);
        }
        else
        {
            envelope["status"] = exitCode == ExitCodes.Cancelled ? "aborted" : "failed";
            envelope["output"] = null;
            envelope["rounds"] = 0;
            envelope["durationMs"] = 0L;
            envelope["usage"] = null;
            envelope["context"] = null;
            envelope["error"] = null;
        }

        await WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RunFailedAsync(
        string? sessionId,
        string error,
        int exitCode,
        CancellationToken cancellationToken = default)
    {
        var envelope = CreateEnvelope("run.failed");
        envelope["sessionId"] = sessionId;
        envelope["status"] = "failed";
        envelope["exitCode"] = exitCode;
        envelope["error"] = error;
        await WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    private JsonObject CreateEnvelope(string type)
    {
        Interlocked.Increment(ref _sequence);
        return new JsonObject
        {
            ["protocol"] = ProtocolVersion,
            ["sequence"] = Volatile.Read(ref _sequence),
            ["type"] = type
        };
    }

    private async ValueTask WriteAsync(JsonObject envelope, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(envelope)).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static JsonSerializerOptions CreateJsonOptions() => new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) }
    };
}

public static class ExitCodes
{
    public const int Success = 0;
    public const int Failed = 1;
    public const int Cancelled = 130;
}
