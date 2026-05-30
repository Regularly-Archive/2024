namespace InsightaAI.LLM.Models;

/// <summary>
/// LLM 请求配置
/// </summary>
public sealed record LlmRequest
{
    /// <summary>模型名称</summary>
    public required string Model { get; init; }

    /// <summary>消息列表</summary>
    public required Message[] Messages { get; init; }

    /// <summary>可用工具</summary>
    public ToolDefinition[]? Tools { get; init; }

    /// <summary>温度 (0.0 - 2.0)</summary>
    public double? Temperature { get; init; }

    /// <summary>最大输出 token 数</summary>
    public int? MaxTokens { get; init; }

    /// <summary>是否启用流式输出</summary>
    public bool Stream { get; init; } = true;

    /// <summary>推理配置 (用于 Claude extended thinking / DeepSeek reasoning)</summary>
    public ReasoningConfig? Reasoning { get; init; }

    /// <summary>停止序列</summary>
    public string[]? StopSequences { get; init; }

    /// <summary>Provider 特定配置</summary>
    public ProviderOptions? ProviderOptions { get; init; }
}

/// <summary>
/// 推理配置 - 统一不同模型的推理能力
/// </summary>
public sealed record ReasoningConfig
{
    /// <summary>是否启用推理</summary>
    public bool Enabled { get; init; }

    /// <summary>推理预算 token 数 (Claude: thinking budget, DeepSeek: 无显式控制)</summary>
    public int? BudgetTokens { get; init; }

    /// <summary>推理努力程度 (OpenAI o1/o3: low/medium/high)</summary>
    public ReasoningEffort? Effort { get; init; }
}

/// <summary>
/// 推理努力程度
/// </summary>
public enum ReasoningEffort
{
    Low,
    Medium,
    High
}

/// <summary>
/// Provider 特定选项
/// </summary>
public sealed record ProviderOptions
{
    /// <summary>OpenAI 特定选项</summary>
    public OpenAIOptions? OpenAI { get; init; }

    /// <summary>Anthropic 特定选项</summary>
    public AnthropicOptions? Anthropic { get; init; }

    /// <summary>自定义选项</summary>
    public Dictionary<string, object>? Custom { get; init; }
}

/// <summary>
/// OpenAI 特定选项
/// </summary>
public sealed record OpenAIOptions
{
    /// <summary>是否使用 Responses API (支持 reasoning)</summary>
    public bool? UseResponsesApi { get; init; }

    /// <summary>Service tier</summary>
    public string? ServiceTier { get; init; }

    /// <summary>用户标识</summary>
    public string? User { get; init; }

    /// <summary>是否允许 LLM 生成并行工具调用，默认 null (使用 API 默认值 true)</summary>
    public bool? ParallelToolCalls { get; init; }
}

/// <summary>
/// Anthropic 特定选项
/// </summary>
public sealed record AnthropicOptions
{
    /// <summary>Top K</summary>
    public int? TopK { get; init; }

    /// <summary>Top P</summary>
    public double? TopP { get; init; }

    /// <summary>元数据</summary>
    public AnthropicMetadata? Metadata { get; init; }
}

/// <summary>
/// Anthropic 元数据
/// </summary>
public sealed record AnthropicMetadata
{
    /// <summary>用户 ID</summary>
    public string? UserId { get; init; }
}
