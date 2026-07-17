using InsightaAI.Agent.Extensions;
using InsightaAI.Agent.Prompts;
using InsightaAI.LLM;
using InsightaAI.LLM.Abstractions;
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

    private readonly Func<string, ILlmClient> _summaryClientFactory;
    private readonly string _summaryModelRef;

    /// <param name="summaryClientFactory">创建摘要 LLM 客户端的工厂，接受 modelId（格式：provider/model）</param>
    /// <param name="summaryModelRef">摘要使用的模型引用，格式：provider/model</param>
    public TraditionalCompactStrategy(Func<string, ILlmClient> summaryClientFactory, string summaryModelRef)
    {
        _summaryClientFactory = summaryClientFactory ?? throw new ArgumentNullException(nameof(summaryClientFactory));
        _summaryModelRef = summaryModelRef ?? throw new ArgumentNullException(nameof(summaryModelRef));
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
        var summary = await GenerateFullSummaryAsync(strippedOldMessages, cancellationToken);

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
    /// 生成全量对话摘要
    /// </summary>
    private async Task<string> GenerateFullSummaryAsync(List<Message> messages, CancellationToken cancellationToken)
    {
        // 从嵌入资源加载摘要提示词
        var summaryPrompt = await PromptTemplate.RenderAsync("traditional-summary");

        // 构建消息列表（摘要提示 + 待摘要的消息）
        var summaryMessages = new List<Message>();
        summaryMessages.AddRange(messages.Where(x => x.Role != MessageRole.System));
        summaryMessages.Add(Message.FromUser(summaryPrompt));

        // 调用 LLM 生成摘要
        var modelName = ModelRef.TryParse(_summaryModelRef, out var modelRef)
            ? modelRef.ModelId
            : _summaryModelRef;
        var summaryClient = _summaryClientFactory(_summaryModelRef);

        var request = new LlmRequest
        {
            Model = modelName,
            Messages = summaryMessages.ToArray(),
            Tools = [], // 不使用工具
            Temperature = 0.3, // 低温度，更确定性的摘要
            MaxTokens = 4096
        };


        var response = await summaryClient.CompleteAsync(request, cancellationToken);
        return ExtractSummary(response.GetTextContent()) ?? "[Summary generation failed]";
    }

    /// <summary>
    /// 从响应中提取摘要内容（支持 XML 标签包裹）
    /// </summary>
    private static string? ExtractSummary(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return null;

        var trimmed = responseText.Trim();

        // 尝试提取 <summary> 标签中的内容
        var summaryStart = trimmed.IndexOf("<summary>", StringComparison.OrdinalIgnoreCase);
        var summaryEnd = trimmed.IndexOf("</summary>", StringComparison.OrdinalIgnoreCase);

        if (summaryStart >= 0 && summaryEnd > summaryStart)
        {
            var start = summaryStart + "<summary>".Length;
            return trimmed[start..summaryEnd].Trim();
        }

        return trimmed;
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
