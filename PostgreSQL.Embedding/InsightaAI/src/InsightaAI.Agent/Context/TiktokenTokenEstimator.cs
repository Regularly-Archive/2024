using Microsoft.ML.Tokenizers;

namespace InsightaAI.Agent.Context;

/// <summary>
/// 基于 Tiktoken 的 Token 估算器（精确，用于 OpenAI 模型）
/// </summary>
public sealed class TiktokenTokenEstimator : ITokenEstimator
{
    private readonly TiktokenTokenizer _tokenizer;

    /// <summary>
    /// 创建指定模型的 Tiktoken 估算器
    /// </summary>
    /// <param name="modelName">模型名称，如 "gpt-4o", "gpt-4", "gpt-3.5-turbo"</param>
    public TiktokenTokenEstimator(string modelName = "gpt-4o")
    {
        _tokenizer = TiktokenTokenizer.CreateForModel(modelName);
    }

    public int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return _tokenizer.CountTokens(text);
    }

    public int EstimateTokens(IEnumerable<string> texts)
    {
        return texts.Sum(EstimateTokens);
    }
}
