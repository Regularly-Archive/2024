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

    /// <summary>工具调用策略，默认 Auto</summary>
    public ToolChoiceMode ToolChoice { get; init; } = ToolChoiceMode.Auto;

    /// <summary>温度 (0.0 - 2.0)</summary>
    public double? Temperature { get; init; }

    /// <summary>最大输出 token 数</summary>
    public int? MaxTokens { get; init; }

    /// <summary>是否启用流式输出</summary>
    public bool Stream { get; init; } = true;

    /// <summary>推理配置 (用于 Claude extended thinking / DeepSeek reasoning)</summary>
    public ReasoningConfig? Reasoning { get; init; }

    /// <summary>
    /// 产品层推理偏好。宿主应在发送请求前按具体模型能力将其解析为 <see cref="Reasoning"/>；
    /// Adapter 不推断模型是否支持某个偏好。
    /// </summary>
    public ReasoningPreference? ReasoningPreference { get; init; }

    /// <summary>
    /// Allows an internal auxiliary request to fall back to <see cref="Models.ReasoningPreference.Default"/>
    /// when the selected model has no mapping for <see cref="ReasoningPreference"/>.
    /// User- and Agent-requested preferences must leave this false.
    /// </summary>
    public bool AllowReasoningFallbackToDefault { get; init; }

    /// <summary>停止序列</summary>
    public string[]? StopSequences { get; init; }

    /// <summary>Provider 特定配置</summary>
    public ProviderOptions? ProviderOptions { get; init; }
}

/// <summary>
/// 推理配置 - 档位表达意图，预算负责落地（详见 docs/architecture/llm-reasoning-control-design.md）
/// </summary>
public sealed record ReasoningConfig
{
    /// <summary>Optional model/deployment-specific Off mapping; null uses conservative adapter defaults.</summary>
    public ReasoningOffMode? OffMode { get; init; }

    /// <summary>控制方式，默认 ProviderDefault（不传思考参数，由供应商默认行为决定）</summary>
    public ReasoningControl Control { get; init; } = ReasoningControl.ProviderDefault;

    /// <summary>推理档位 (Control=Effort 时必填)</summary>
    public ReasoningEffortLevel? Effort { get; init; }

    /// <summary>原生预算 token 数 (Control=Budget 时必填；Anthropic budget_tokens / Gemini thinkingBudget)</summary>
    public int? BudgetTokens { get; init; }

    public static ReasoningConfig Off() => new() { Control = ReasoningControl.Off };

    public static ReasoningConfig WithEffort(ReasoningEffortLevel level) =>
        new() { Control = ReasoningControl.Effort, Effort = level };

    public static ReasoningConfig WithBudget(int budgetTokens) =>
        new() { Control = ReasoningControl.Budget, BudgetTokens = budgetTokens };

    /// <summary>校验配置完整性；适配器在翻译前调用，消灭隐式默认</summary>
    public void Validate()
    {
        switch (Control)
        {
            case ReasoningControl.Effort when Effort is null:
                throw new InvalidOperationException("ReasoningControl.Effort requires an Effort level.");
            case ReasoningControl.Budget when BudgetTokens is not > 0:
                throw new InvalidOperationException("ReasoningControl.Budget requires positive BudgetTokens.");
        }
    }
}

/// <summary>
/// 面向用户和 Agent 配置的稳定推理偏好。
/// 原生 effort、budget 和关闭协议仅存在于模型能力映射中。
/// </summary>
public enum ReasoningPreference
{
    /// <summary>不发送控制字段，遵循模型或供应商默认行为。</summary>
    Default,

    /// <summary>偏好较低延迟和推理开销。</summary>
    Fast,

    /// <summary>明确要求关闭思考。</summary>
    Off,

    /// <summary>偏好质量、延迟与成本之间的均衡。</summary>
    Balance,

    /// <summary>偏好更充分的推理和质量。</summary>
    Deep
}

/// <summary>
/// 推理控制方式
/// </summary>
public enum ReasoningControl
{
    /// <summary>明确关闭思考（需要显式传参的模型由适配器传参，如 Gemini thinkingBudget=0）</summary>
    Off,

    /// <summary>不传任何思考参数，由供应商默认行为决定</summary>
    ProviderDefault,

    /// <summary>按档位驱动思考强度；预算型模型由适配器映射表翻译为原生预算</summary>
    Effort,

    /// <summary>按原生预算直通（需精确控制成本的逃生舱场景）</summary>
    Budget
}

/// <summary>
/// 推理档位 - 跨供应商公共子集（none~high，Vercel AI SDK 同款）加扩展档位（xhigh）
/// </summary>
public enum ReasoningEffortLevel
{
    /// <summary>档位驱动的关闭（OpenAI effort=none；对预算型模型视同 Off）</summary>
    None,

    /// <summary>比 low 更轻的思考（OpenAI minimal）</summary>
    Minimal,

    /// <summary>轻量思考</summary>
    Low,

    /// <summary>中等思考</summary>
    Medium,

    /// <summary>深度思考</summary>
    High,

    /// <summary>超深度思考（仅部分模型支持；不支持时由能力矩阵降级，M2）</summary>
    XHigh
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

/// <summary>
/// 工具调用策略
/// </summary>
public enum ToolChoiceMode
{
    /// <summary>自动决定是否调用工具（默认值）</summary>
    Auto,

    /// <summary>禁止调用工具</summary>
    None
}
