namespace InsightaAI.Agent.Context;

/// <summary>
/// 上下文预算配置
/// </summary>
public sealed record ContextBudget
{
    /// <summary>
    /// 模型上下文窗口大小（token 数）
    /// </summary>
    /// <remarks>
    /// 优先级：用户配置 > API 元数据 > ModelContextWindows 映射 > 默认值 (128K)
    /// </remarks>
    public int MaxContextTokens { get; init; } = 128_000;

    /// <summary>
    /// Level 1 MicroCompact 触发阈值百分比
    /// </summary>
    public double MicroCompactThreshold { get; init; } = 0.60;

    /// <summary>
    /// Level 2 SessionMemoryCompact 触发阈值百分比
    /// </summary>
    public double SessionCompactThreshold { get; init; } = 0.65;

    /// <summary>
    /// Level 3 TraditionalCompact 触发阈值百分比
    /// </summary>
    public double TraditionalCompactThreshold { get; init; } = 0.75;

    /// <summary>
    /// 预留给模型输出的 token 数
    /// </summary>
    public int ReservedForOutput { get; init; } = 16_384;

    /// <summary>
    /// MicroCompact: 保留完整内容的最近工具结果数量
    /// </summary>
    public int KeepRecentToolResults { get; init; } = 5;

    /// <summary>
    /// TraditionalCompact: 保留的最近消息轮次数
    /// </summary>
    public int KeepRecentRounds { get; init; } = 10;

    /// <summary>
    /// 压缩后恢复的最大文件数
    /// </summary>
    public int MaxFilesToRestore { get; init; } = 5;

    /// <summary>
    /// 恢复文件的 token 预算
    /// </summary>
    public int FileRestoreTokenBudget { get; init; } = 50_000;

    /// <summary>
    /// 摘要模型（可选，默认使用当前模型，可配置更便宜的模型）
    /// </summary>
    public string? SummaryModel { get; init; }

    /// <summary>
    /// 是否启用上下文压缩
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 获取 MicroCompact 的实际触发 token 数
    /// </summary>
    public int MicroCompactTriggerTokens => (int)(MaxContextTokens * MicroCompactThreshold);

    /// <summary>
    /// 获取 SessionMemoryCompact 的实际触发 token 数
    /// </summary>
    public int SessionCompactTriggerTokens => (int)(MaxContextTokens * SessionCompactThreshold);

    /// <summary>
    /// 获取 TraditionalCompact 的实际触发 token 数
    /// </summary>
    public int TraditionalCompactTriggerTokens => (int)(MaxContextTokens * TraditionalCompactThreshold);

    /// <summary>
    /// 获取可用的输入 token 数（上下文窗口 - 预留输出）
    /// </summary>
    public int AvailableInputTokens => MaxContextTokens - ReservedForOutput;
}

/// <summary>
/// 模型上下文窗口映射（硬编码 fallback）
/// </summary>
public static class ModelContextWindows
{
    /// <summary>
    /// 已知模型的上下文窗口大小
    /// </summary>
    public static readonly Dictionary<string, int> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        // OpenAI
        ["gpt-4o"] = 128_000,
        ["gpt-4o-mini"] = 128_000,
        ["gpt-4-turbo"] = 128_000,
        ["gpt-4"] = 8_192,
        ["gpt-3.5-turbo"] = 16_385,

        // Anthropic
        ["claude-sonnet-4-20250514"] = 200_000,
        ["claude-3-5-sonnet-20241022"] = 200_000,
        ["claude-3-5-haiku-20241022"] = 200_000,
        ["claude-3-opus-20240229"] = 200_000,

        // Google
        ["gemini-2.0-flash"] = 1_048_576,
        ["gemini-1.5-pro"] = 2_097_152,
        ["gemini-1.5-flash"] = 1_048_576,

        // DeepSeek
        ["deepseek-chat"] = 128_000,
        ["deepseek-reasoner"] = 128_000,
    };

    /// <summary>
    /// 获取模型的上下文窗口大小
    /// </summary>
    /// <param name="model">模型名称</param>
    /// <param name="defaultSize">未找到时的默认值</param>
    /// <returns>上下文窗口大小（token 数）</returns>
    public static int GetContextWindowSize(string model, int defaultSize = 128_000)
    {
        if (string.IsNullOrEmpty(model))
            return defaultSize;

        // 尝试精确匹配
        if (Defaults.TryGetValue(model, out var size))
            return size;

        // 尝试前缀匹配（例如 "gpt-4o-2024-08-06" 匹配 "gpt-4o"）
        foreach (var (key, value) in Defaults)
        {
            if (model.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return defaultSize;
    }
}
