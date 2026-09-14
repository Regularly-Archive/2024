using System.Text.Json;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Evals;

/// <summary>Deterministic LLM used by replay scenarios; the Agent Loop and tools remain real.</summary>
public sealed class RecordedLlmClient(IReadOnlyList<RecordedLlmStep> steps) : ILlmClient
{
    private readonly IReadOnlyList<RecordedLlmStep> _steps = steps;
    private int _requestCount;

    public int RequestCount => _requestCount;
    public string AdapterName => "recorded";
    public bool SupportsReasoning => false;

    public LlmStream Streaming(LlmRequest request)
    {
        var index = Interlocked.Increment(ref _requestCount) - 1;
        if (index >= _steps.Count)
            throw new InvalidOperationException("Replay transcript ended before the Agent completed its turn.");

        return new LlmStreamImpl(CreateEventsAsync(_steps[index], request.Model));
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        using var stream = Streaming(request);
        return await stream.GetResponseAsync(cancellationToken);
    }

    public void Dispose() { }

    private static async IAsyncEnumerable<StreamEvent> CreateEventsAsync(RecordedLlmStep step, string model)
    {
        yield return new StreamStartEvent { Model = model, Provider = "recorded" };

        if (!string.IsNullOrEmpty(step.Text))
            yield return new TextDeltaEvent { Delta = step.Text, ContentIndex = 0 };

        for (var index = 0; index < step.ToolCalls.Count; index++)
        {
            var toolCall = step.ToolCalls[index];
            yield return new ToolCallStartEvent
            {
                ContentIndex = index,
                ToolName = toolCall.Name,
                ToolCallId = toolCall.Id
            };
            yield return new ToolCallDeltaEvent
            {
                ContentIndex = index,
                ArgumentsDelta = JsonSerializer.Serialize(toolCall.Arguments)
            };
        }

        yield return new DoneEvent
        {
            Reason = step.ToolCalls.Count > 0 ? DoneReason.ToolCalls : DoneReason.Complete
        };

        await Task.CompletedTask;
    }
}
