using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>Applies the configured model's reasoning capability before the request reaches an adapter.</summary>
public sealed class ModelConfiguredLlmClient : ILlmClient
{
    private readonly ILlmClient _inner;
    private readonly ModelReasoningResolver _resolver;

    public ModelConfiguredLlmClient(ILlmClient inner, ModelReasoningResolver resolver)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public string AdapterName => _inner.AdapterName;
    public bool SupportsReasoning => _inner.SupportsReasoning;

    public LlmStream Streaming(LlmRequest request) => _inner.Streaming(Resolve(request));

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default) =>
        _inner.CompleteAsync(Resolve(request), cancellationToken);

    public void Dispose() => _inner.Dispose();

    private LlmRequest Resolve(LlmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ReasoningPreference is not { } preference)
            return request;
        if (request.Reasoning != null)
            throw new InvalidOperationException("A request cannot specify both a reasoning preference and a native reasoning configuration.");

        if (request.AllowReasoningFallbackToDefault && !_resolver.Supports(preference))
        {
            return request with
            {
                ReasoningPreference = null,
                AllowReasoningFallbackToDefault = false
            };
        }

        return request with
        {
            ReasoningPreference = null,
            Reasoning = _resolver.Resolve(preference),
            AllowReasoningFallbackToDefault = false
        };
    }
}
