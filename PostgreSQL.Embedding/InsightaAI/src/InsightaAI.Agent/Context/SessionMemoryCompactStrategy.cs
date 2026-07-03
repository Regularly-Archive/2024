using InsightaAI.Agent.Memory;
using InsightaAI.Agent.Prompts;
using InsightaAI.LLM.Models;
using System.Text.Json;

namespace InsightaAI.Agent.Context;

/// <summary>
/// 会话记忆压缩策略 - 使用预提取的会话记忆作为摘要（Level 2）
/// </summary>
/// <remarks>
/// 策略逻辑：
/// 1. 从 SessionMemoryHook 获取预提取的会话记忆
/// 2. 分离消息：系统消息 + 旧消息（丢弃）+ 最近 N 轮（保留）
/// 3. 使用会话记忆作为压缩后的摘要（零 LLM 成本）
/// 4. 构建压缩后的消息列表
///
/// 优势：
/// - 零 LLM 调用成本
/// - 即时执行（无需等待 LLM 响应）
/// - 利用后台预提取的记忆内容
/// </remarks>
public sealed class SessionMemoryCompactStrategy : ICompactStrategy
{
    public string Name => "SessionMemoryCompact";
    public int Priority => 2; // 介于 MicroCompact(1) 和 TraditionalCompact(3) 之间

    private readonly SessionMemoryHook _sessionMemoryHook;
    private readonly string _memoryFilePath;
    private readonly string _memoryMetadataFilePath;

    public SessionMemoryCompactStrategy(SessionMemoryHook sessionMemoryHook)
    {
        _sessionMemoryHook = sessionMemoryHook ?? throw new ArgumentNullException(nameof(sessionMemoryHook));
        _memoryFilePath = Path.Combine(sessionMemoryHook.SessionDirectory, "MEMORY.md");
        _memoryMetadataFilePath = Path.Combine(sessionMemoryHook.SessionDirectory, "metadata.json");
    }

    public bool ShouldCompact(IReadOnlyList<Message> messages, int estimatedTokens, ContextBudget budget)
    {
        //// 检查是否达到会话记忆压缩阈值
        if (estimatedTokens < budget.SessionCompactTriggerTokens)
            return false;

        // 同步检查文件是否存在（避免 .GetAwaiter().GetResult() 导致的死锁风险）
        return File.Exists(_memoryFilePath);
    }

    public async Task<CompactionResult> CompactAsync(
        List<Message> messages,
        ContextBudget budget,
        ITokenEstimator tokenEstimator,
        int preCompactTokens,
        CancellationToken cancellationToken = default)
    {
        var preCompactMessages = messages.Count;

        try
        {
            // Step 1: 获取会话记忆
            var sessionMemory = await _sessionMemoryHook.GetSessionMemoryAsync(cancellationToken);

            // 如果会话记忆为空，返回不压缩的结果
            if (string.IsNullOrWhiteSpace(sessionMemory))
            {
                return CreateNoCompactionResult(preCompactTokens, preCompactMessages, messages);
            }

            // Step 2: 分离消息
            var (systemMessages, _, recentMessages) = SplitMessages(messages, budget.KeepRecentRounds);

            // Step 3: 构建边界标记
            var boundaryMarker = await CreateCompactedContextBoundaryMarkerAsync(sessionMemory, preCompactTokens, preCompactMessages);

            // Step 4: 构建压缩后的消息列表
            var compactedMessages = new List<Message>();

            // 添加系统消息
            compactedMessages.AddRange(systemMessages);

            // 添加边界标记（作为系统消息)
            var updatedSystemMessage = Message.FromSystem(systemMessages[0].GetTextContent() + boundaryMarker);
            compactedMessages = [updatedSystemMessage, .. compactedMessages[1..]];


            // 添加最近的消息
            compactedMessages.AddRange(recentMessages);

            // 估算压缩后的 token
            var postCompactTokens = EstimateMessagesTokens(compactedMessages, tokenEstimator);

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 文件读取失败时，记录错误并返回不压缩的结果
            System.Diagnostics.Debug.WriteLine($"[SessionMemoryCompact] Failed to read session memory: {ex.Message}");
            return CreateNoCompactionResult(preCompactTokens, preCompactMessages, messages);
        }
    }

    /// <summary>
    /// 创建不压缩的结果（错误恢复时使用）
    /// </summary>
    private static CompactionResult CreateNoCompactionResult(int preCompactTokens, int preCompactMessages, List<Message> messages)
    {
        return new CompactionResult
        {
            StrategyName = "SessionMemoryCompact",
            PreCompactTokens = preCompactTokens,
            PostCompactTokens = preCompactTokens,
            PreCompactMessages = preCompactMessages,
            PostCompactMessages = messages.Count,
            RequestMessages = messages.ToArray()
        };
    }

    /// <summary>
    /// 分离消息为：系统消息、旧消息、最近消息
    /// </summary>
    private (List<Message> System, List<Message> Old, List<Message> Recent) SplitMessages(
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
        int recentStartIndex = FindRecentMessagesStart(otherMessages, keepRecentRounds);

        var oldMessages = otherMessages.Take(recentStartIndex).ToList();
        var recentMessages = otherMessages.Skip(recentStartIndex).ToList();

        return (systemMessages, oldMessages, recentMessages);
    }

    /// <summary>
    /// 找到最近消息的起始索引
    /// 优先按时间戳找（元数据可用时），否则按轮数找
    /// </summary>
    private int FindRecentMessagesStart(List<Message> messages, int keepRecentRounds)
    {
        var metadata = LoadSessionMemoryMetadata();
        int startIndex = messages.Count;

        // 优先按时间戳找（元数据的 UpdatedAt 有值时）
        if (metadata?.UpdatedAt != default)
        {
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].Role == MessageRole.User)
                {
                    var userMessage = messages[i];
                    if (userMessage.Timestamp <= metadata.UpdatedAt)
                    {
                        startIndex = i;
                        break;
                    }
                }
            }
        }
        else
        {
            // 元数据不可用时，按轮数找
            int userCount = 0;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].Role == MessageRole.User)
                {
                    userCount++;
                    if (userCount >= keepRecentRounds)
                    {
                        startIndex = i;
                        break;
                    }
                }
            }
        }

        return startIndex;
    }

    /// <summary>
    /// 创建压缩边界标记
    /// </summary>
    private async Task<string> CreateCompactedContextBoundaryMarkerAsync(string sessionMemory, int preCompactTokens, int preCompactMessages)
    {
        return await PromptTemplate.RenderAsync("compacted-context", new Dictionary<string, string>
        {
            ["preCompactMessages"] = preCompactMessages.ToString(),
            ["preCompactTokens"] = preCompactTokens.ToString("N0"),
            ["sessionMemory"] = sessionMemory

        });
    }

    /// <summary>
    /// 估算消息列表的 token 数量
    /// </summary>
    private int EstimateMessagesTokens(List<Message> messages, ITokenEstimator tokenEstimator)
    {
        int total = 0;
        foreach (var message in messages)
        {
            total += 4; // 消息开销
            foreach (var block in message.Content)
            {
                if (block is TextBlock textBlock)
                    total += tokenEstimator.EstimateTokens(textBlock.Text);
                else if (block is ThinkingBlock thinkingBlock)
                    total += tokenEstimator.EstimateTokens(thinkingBlock.Thinking);
                else if (block is ImageBlock)
                    total += 2000;
                else if (block is ToolCallBlock toolCall)
                {
                    total += tokenEstimator.EstimateTokens(toolCall.Name);
                    total += tokenEstimator.EstimateTokens(toolCall.Arguments.GetRawText());
                }
                else if (block is ToolResultBlock toolResult)
                {
                    foreach (var content in toolResult.Content)
                    {
                        if (content is TextBlock text)
                            total += tokenEstimator.EstimateTokens(text.Text);
                        else if (content is ImageBlock)
                            total += 2000;
                    }
                }
            }
        }
        return total;
    }

    /// <summary>
    /// 加载会话记忆元数据
    /// </summary>
    /// <returns></returns>
    private SessionMemoryMetadata? LoadSessionMemoryMetadata()
    {
        try
        {
            if (!File.Exists(_memoryMetadataFilePath)) return null;

            var json = File.ReadAllText(_memoryMetadataFilePath);
            return JsonSerializer.Deserialize<SessionMemoryMetadata>(json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
