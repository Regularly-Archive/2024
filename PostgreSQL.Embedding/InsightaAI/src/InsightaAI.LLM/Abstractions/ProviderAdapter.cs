using System.Text.Json;
using InsightaAI.LLM.Models;

namespace InsightaAI.LLM.Abstractions;

/// <summary>
/// Provider 配置
/// </summary>
public sealed record ProviderConfig
{
    /// <summary>API Key</summary>
    public required string ApiKey { get; init; }

    /// <summary>API Base URL</summary>
    public string? BaseUrl { get; init; }

    /// <summary>自定义请求头</summary>
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>超时时间</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>最大重试次数</summary>
    public int? MaxRetries { get; init; }
}

/// <summary>
/// Provider 适配器接口 - 负责将统一请求转换为特定 Provider 格式并解析响应
/// </summary>
public interface IProviderAdapter
{
    /// <summary>Provider 名称</summary>
    string Name { get; }

    /// <summary>是否支持推理能力</summary>
    bool SupportsReasoning { get; }

    /// <summary>支持的推理模式</summary>
    ReasoningMode SupportedReasoningModes { get; }

    /// <summary>
    /// 将统一请求转换为 Provider 特定的 HTTP 请求
    /// </summary>
    HttpRequestMessage CreateRequest(LlmRequest request, ProviderConfig config, bool stream);

    /// <summary>
    /// 解析 SSE 流事件为统一事件
    /// </summary>
    StreamEvent? ParseStreamEvent(string eventType, JsonElement data);

    /// <summary>
    /// 解析完整响应为统一响应
    /// </summary>
    LlmResponse ParseResponse(JsonElement response);
}

/// <summary>
/// 推理模式
/// </summary>
[Flags]
public enum ReasoningMode
{
    /// <summary>不支持推理</summary>
    None = 0,

    /// <summary>Claude extended thinking 模式</summary>
    ExtendedThinking = 1,

    /// <summary>OpenAI reasoning effort 模式 (o1, o3)</summary>
    ReasoningEffort = 2,

    /// <summary>DeepSeek reasoning_content 模式</summary>
    ReasoningContent = 4,

    /// <summary>所有模式</summary>
    All = ExtendedThinking | ReasoningEffort | ReasoningContent
}
