using InsightaAI.LLM.Models;

namespace InsightaAI.LLM.Abstractions;

/// <summary>
/// LLM 客户端接口
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// 发起流式请求
    /// </summary>
    LlmStream Stream(LlmRequest request);

    /// <summary>
    /// 发起非流式请求 (内部可能使用流式)
    /// </summary>
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Provider 名称
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Provider 是否支持推理 (thinking/reasoning)
    /// </summary>
    bool SupportsReasoning { get; }
}
