namespace InsightaAI.Agent.Context;

/// <summary>
/// Token 估算器接口
/// </summary>
public interface ITokenEstimator
{
    /// <summary>
    /// 估算文本的 token 数量
    /// </summary>
    int EstimateTokens(string text);

    /// <summary>
    /// 估算多个文本的 token 数量
    /// </summary>
    int EstimateTokens(IEnumerable<string> texts);
}
