using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>Applies CLI model reasoning capability resolution to an outgoing LLM request.</summary>
public sealed class ModelReasoningMiddleware(ModelReasoningResolver resolver) : ILlmRequestMiddleware
{
    private readonly ModelReasoningResolver _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

    public LlmRequest Invoke(LlmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ReasoningPreference is not { } preference)
            return request;
        if (request.Reasoning != null)
            throw new InvalidOperationException("A request cannot specify both a reasoning preference and a native reasoning configuration.");

        if (request.AllowReasoningFallbackToDefault && !_resolver.Supports(preference))
            return LlmRequest.WithResolvedReasoning(request, reasoning: null);

        return LlmRequest.WithResolvedReasoning(request, _resolver.Resolve(preference));
    }
}
