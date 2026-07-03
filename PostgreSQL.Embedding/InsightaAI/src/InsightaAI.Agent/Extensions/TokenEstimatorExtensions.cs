using InsightaAI.Agent.Context;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Extensions;

/// <summary>
/// Token 估算器扩展方法
/// </summary>
public static class TokenEstimatorExtensions
{
    /// <summary>
    /// 估算消息列表的 token 数量
    /// </summary>
    public static int EstimateMessagesTokens(this CharTokenEstimator estimator, List<Message> messages)
    {
        int total = 0;
        foreach (var message in messages)
        {
            total += 4; // 消息开销
            foreach (var block in message.Content)
            {
                total += block switch
                {
                    TextBlock text => estimator.EstimateTokens(text.Text),
                    ThinkingBlock thinking => estimator.EstimateTokens(thinking.Thinking),
                    ImageBlock => 2000,
                    ToolCallBlock toolCall => estimator.EstimateTokens(toolCall.Name)
                                             + estimator.EstimateTokens(toolCall.Arguments.GetRawText()),
                    ToolResultBlock toolResult => EstimateToolResultTokens(estimator, toolResult),
                    _ => 0
                };
            }
        }
        return total;
    }

    private static int EstimateToolResultTokens(ITokenEstimator estimator, ToolResultBlock toolResult)
    {
        int total = 0;
        foreach (var content in toolResult.Content)
        {
            total += content switch
            {
                TextBlock text => estimator.EstimateTokens(text.Text),
                ImageBlock => 2000,
                _ => 0
            };
        }
        return total;
    }
}
