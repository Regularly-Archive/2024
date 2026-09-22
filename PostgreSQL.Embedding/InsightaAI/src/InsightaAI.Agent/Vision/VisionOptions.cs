using InsightaAI.LLM.Abstractions;

namespace InsightaAI.Agent.Vision;

/// <summary>
/// 视觉模型调用配置。由宿主（Chat/Run）在装配 Agent 时注册到服务容器。
/// 未注册时视觉工具显式报错，不静默降级到主模型。
/// </summary>
public sealed class VisionOptions
{
    /// <summary>视觉模型引用（provider/model 格式，如 "zhipu/glm-4.5v"）。</summary>
    public required string Model { get; init; }

    /// <summary>按模型引用创建 LLM 客户端的工厂。</summary>
    public required Func<string, ILlmClient> ClientFactory { get; init; }

    /// <summary>单次描述的最大输出 token 数。</summary>
    public int MaxTokens { get; init; } = 1024;

    /// <summary>采样温度。</summary>
    public double Temperature { get; init; } = 0.3;
}
