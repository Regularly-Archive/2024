using InsightaAI.Agent.Extensions;
using InsightaAI.Agent.Context.Summary;
using InsightaAI.Agent.Prompts;
using InsightaAI.LLM.Models;
using System.Collections.Immutable;

namespace InsightaAI.Agent.Context.Compaction;

/// <summary>
/// 传统压缩策略 - 使用 LLM 生成对话摘要
/// </summary>
/// <remarks>
/// 策略逻辑：
/// 1. 分离消息：系统消息 + 旧消息（待压缩）+ 最近 N 轮（保留）
/// 2. 剥离图片：减少摘要生成的 token 成本
/// 3. LLM 摘要：生成结构化的对话摘要
/// 4. 构建压缩后的消息列表
/// </remarks>
public sealed class TraditionalCompactStrategy : ICompactStrategy
{
    public string Name => "TraditionalCompact";
    public int Priority => 3; // 低于 MicroCompact(1) 和 SessionMemoryCompact(2)

    private readonly ISummaryService _summaryService;

    public TraditionalCompactStrategy(ISummaryService summaryService)
    {
        _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
    }

    public bool ShouldCompact(IReadOnlyList<Message> messages, int estimatedTokens, ContextBudget budget)
    {
        // 检查是否达到传统压缩阈值
        return estimatedTokens >= budget.TraditionalCompactTriggerTokens;
    }

    public async Task<CompactionResult> CompactAsync(
        List<Message> messages,
        ContextBudget budget,
        ITokenEstimator tokenEstimator,
        int preCompactTokens,
        CancellationToken cancellationToken = default)
    {
        var preCompactMessages = messages.Count;

        // Step 1: 分离消息
        var (systemMessages, oldMessages, recentMessages) = SplitMessages(messages, budget.KeepRecentRounds);

        // Step 2: 剥离图片
        var strippedOldMessages = StripImages(oldMessages);
        if (!strippedOldMessages.Any()) return CreateNoCompactionResult(preCompactTokens, preCompactMessages, messages);

        // Step 3: 生成摘要
        var summaryResult = await _summaryService.SummarizeAsync(strippedOldMessages, cancellationToken);
        if (!summaryResult.Success || string.IsNullOrWhiteSpace(summaryResult.Summary))
            return CreateNoCompactionResult(preCompactTokens, preCompactMessages, messages);

        var summary = summaryResult.Summary;

        // Step 4: 构建边界标记
        var boundaryMarker = await CreateCompactedContextBoundaryMarkerAsync(summary, preCompactTokens, preCompactMessages);

        // Step 5: 构建压缩后的消息列表
        var compactedMessages = new List<Message>();

        // 添加系统消息
        compactedMessages.AddRange(systemMessages);

        // 添加边界标记（作为系统消息）
        compactedMessages.Add(Message.FromAssistant(boundaryMarker));

        // 添加最近的消息
        compactedMessages.AddRange(recentMessages);

        // 估算压缩后的 token
        var postCompactTokens = (tokenEstimator as CharTokenEstimator).EstimateMessagesTokens(compactedMessages);

        // 更新原始消息列表
        messages.Clear();
        messages.AddRange(compactedMessages);

        return new CompactionResult
        {
            StrategyName = Name,
            PreCompactTokens = preCompactTokens,
            PostCompactTokens = postCompactTokens,
            PreCompactMessages = preCompactMessages,
            PostCompactMessages = compactedMessages.Count,
            RequestMessages = compactedMessages.ToArray()
        };
    }

    /// <summary>
    /// 分离消息为：系统消息、旧消息、最近消息
    /// </summary>
    private static (List<Message> System, List<Message> Old, List<Message> Recent) SplitMessages(
        List<Message> messages, int keepRecentRounds)
    {
        var systemMessages = new List<Message>();
        var otherMessages = new List<Message>();

        // 分离系统消息
        foreach (var message in messages)
        {
            if (message.Role == MessageRole.System)
                systemMessages.Add(message);
            else
                otherMessages.Add(message);
        }

        // 计算最近消息的起始索引
        // 一轮 = 用户消息 + 助手响应（可能包含工具调用）
        int recentStartIndex = FindRecentMessagesStart(otherMessages, keepRecentRounds);

        var oldMessages = otherMessages.Take(recentStartIndex).ToList();
        var recentMessages = otherMessages.Skip(recentStartIndex).ToList();

        return (systemMessages, oldMessages, recentMessages);
    }

    /// <summary>
    /// 找到最近消息的起始索引
    /// </summary>
    private static int FindRecentMessagesStart(List<Message> messages, int keepRecentRounds)
    {
        int roundCount = 0;
        int startIndex = messages.Count;

        // 从后往前数，找到 keepRecentRounds 轮的起始位置
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == MessageRole.User)
            {
                roundCount++;
                if (roundCount >= keepRecentRounds)
                {
                    startIndex = i;
                    break;
                }
            }
        }

        return startIndex;
    }

    /// <summary>
    /// 剥离图片，替换为标记
    /// </summary>
    private static List<Message> StripImages(List<Message> messages)
    {
        var result = new List<Message>();

        foreach (var message in messages)
        {
            var newContent = new List<ContentBlock>();

            foreach (var block in message.Content)
            {
                if (block is ImageBlock)
                {
                    // 替换图片为文本标记
                    newContent.Add(new TextBlock { Text = "[image]" });
                }
                else
                {
                    newContent.Add(block);
                }
            }

            result.Add(new Message
            {
                Role = message.Role,
                Content = newContent.ToArray(),
                ToolCallId = message.ToolCallId,
                ToolName = message.ToolName,
                Timestamp = message.Timestamp
            });
        }

        return result;
    }

    /// <summary>
    /// 创建压缩边界标记
    /// </summary>
    private async Task<string> CreateCompactedContextBoundaryMarkerAsync(string summary, int preCompactTokens, int preCompactMessages)
    {
        return await PromptTemplate.RenderAsync("compacted-context", new Dictionary<string, string>
        {
            ["compactStrategy"] = Name,
            ["preCompactMessages"] = preCompactMessages.ToString(),
            ["preCompactTokens"] = preCompactTokens.ToString("N0"),
            ["sessionMemory"] = summary,
        });
    }

    private CompactionResult CreateNoCompactionResult(int preCompactTokens, int preCompactMessages, List<Message> messages)
    {
        return new CompactionResult
        {
            StrategyName = Name,
            PreCompactTokens = preCompactTokens,
            PostCompactTokens = preCompactTokens,
            PreCompactMessages = preCompactMessages,
            PostCompactMessages = messages.Count,
            RequestMessages = messages.ToArray()
        };
    }
}
