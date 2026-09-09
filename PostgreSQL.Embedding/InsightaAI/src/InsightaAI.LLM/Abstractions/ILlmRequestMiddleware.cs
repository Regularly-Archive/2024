using InsightaAI.LLM.Models;

namespace InsightaAI.LLM.Abstractions;

/// <summary>
/// Transforms or validates an LLM request before it is translated by a provider adapter.
/// Implementations must not perform network I/O.
/// </summary>
public interface ILlmRequestMiddleware
{
    LlmRequest Invoke(LlmRequest request);
}
