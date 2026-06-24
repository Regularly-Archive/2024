using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Prompts;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Memory;

/// <summary>
/// SessionMemoryHook 配置选项
/// </summary>
public sealed record SessionMemoryOptions
{
    /// <summary>是否启用 LLM 增强摘要</summary>
    public bool EnableLlmSummary { get; init; } = true;

    /// <summary>启用 LLM 摘要的最小轮次数</summary>
    public int MinRoundsBeforeLlm { get; init; } = 3;

    /// <summary>LLM 摘要的轮次间隔</summary>
    public int SummaryInterval { get; init; } = 1;

    /// <summary>摘要使用的模型</summary>
    public string SummaryModel { get; init; } = "deepseek-v4-flash";

    /// <summary>摘要最大 token 数</summary>
    public int SummaryMaxTokens { get; init; } = 512;

    /// <summary>摘要温度</summary>
    public double SummaryTemperature { get; init; } = 0.3;
}

/// <summary>
/// 会话记忆钩子 - 在每轮结束后提取短期记忆，支持 LLM 增强摘要
///
/// 存储结构：
/// ~/.insightai/memory/sessions/{sessionId}/
/// ├── session-memory.md    # 会话级记忆（短期）
/// └── metadata.json        # 会话元数据
/// </summary>
public sealed class SessionMemoryHook : IAgentHook
{
    private readonly string _sessionId;
    private readonly string _userId;
    private readonly string? _projectId;
    private readonly string _sessionDir;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // LLM 增强配置
    private readonly SessionMemoryOptions _options;

    /// <summary>
    /// Hook 唯一标识
    /// </summary>
    public string Id => "session-memory";

    /// <summary>
    /// 会话 ID
    /// </summary>
    public string SessionId => _sessionId;

    /// <summary>
    /// 会话记忆目录路径
    /// </summary>
    public string SessionDirectory => _sessionDir;

    /// <summary>
    /// 创建 SessionMemoryHook 实例
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="userId">用户 ID</param>
    /// <param name="projectId">项目 ID（可选）</param>
    /// <param name="options">配置选项（可选）</param>
    public SessionMemoryHook(
        string sessionId,
        string userId,
        string? projectId = null,
        SessionMemoryOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentException.ThrowIfNullOrEmpty(userId);

        _options = options ?? new SessionMemoryOptions();

        if (_options.SummaryInterval < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "SummaryInterval must be >= 1");
        if (_options.SummaryMaxTokens < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "SummaryMaxTokens must be >= 1");

        _sessionId = sessionId;
        _userId = userId;
        _projectId = projectId;

        // 会话记忆目录
        var memoryBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".insightai", "memory", "sessions", sessionId);
        _sessionDir = memoryBase;
        Directory.CreateDirectory(_sessionDir);
    }

    /// <summary>
    /// 每轮结束后触发，异步提取记忆
    /// </summary>
    public Task OnRoundEndAsync(
        HookContext context,
        int round,
        IReadOnlyList<Message> messages,
        Message? assistantMessage,
        CancellationToken cancellationToken = default)
    {
        // 创建快照：后台任务可能在 Agent 主循环修改 messages 之后才执行
        var messagesSnapshot = messages.ToList();

        // 在后台执行提取，不传递 cancellationToken（调用方可能已取消）
        _ = Task.Run(async () =>
        {
            try
            {
                await ExtractAndSaveMemoryAsync(context, round, messagesSnapshot, assistantMessage, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // 记忆提取失败不应影响对话
                System.Diagnostics.Debug.WriteLine($"[SessionMemory] Round {round} extraction failed: {ex.Message}");
            }
        });

        // 立即返回，不等待后台任务完成
        return Task.CompletedTask;
    }

    /// <summary>
    /// 会话结束时触发
    /// </summary>
    public async Task OnSessionEndAsync(
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken = default)
    {
        // 更新元数据
        await SaveMetadataAsync(cancellationToken);
    }

    /// <summary>
    /// 获取会话级记忆内容（用于 L2 压缩）
    /// </summary>
    public async Task<string> GetSessionMemoryAsync(CancellationToken cancellationToken = default)
    {
        var memoryPath = Path.Combine(_sessionDir, "session-memory.md");
        if (!File.Exists(memoryPath))
            return "";

        return await File.ReadAllTextAsync(memoryPath, cancellationToken);
    }

    /// <summary>
    /// 提取并保存记忆
    /// </summary>
    private async Task ExtractAndSaveMemoryAsync(
        HookContext context,
        int round,
        IReadOnlyList<Message> messages,
        Message? assistantMessage,
        CancellationToken cancellationToken)
    {
        // Step 0: 检查是否满足 LLM 摘要条件
        if (_options.EnableLlmSummary
            && round >= _options.MinRoundsBeforeLlm
            && (round - _options.MinRoundsBeforeLlm) % _options.SummaryInterval == 0
            && context.LlmClient != null)
        {
            // Step 1: 读取已有摘要
            var existingSummary = await GetSessionMemoryAsync(cancellationToken);

            // Step 2: 使用 LLM 锚定增量摘要（读取旧摘要 → 合并新事实 → 替换文件）
            var mergedSummary = await GenerateLlmSummaryAsync(
                context.LlmClient, existingSummary, messages, cancellationToken);

            if (!string.IsNullOrWhiteSpace(mergedSummary))
            {
                // Step 3: 替换文件（不是追加）
                await _lock.WaitAsync(cancellationToken);
                try
                {
                    var memoryPath = Path.Combine(_sessionDir, "session-memory.md");
                    await File.WriteAllTextAsync(memoryPath, mergedSummary, cancellationToken);
                }
                finally
                {
                    _lock.Release();
                }
                return;
            }

            // LLM 失败，降级到关键词提取
            System.Diagnostics.Debug.WriteLine($"[SessionMemory] LLM summary empty, falling back to keyword extraction");
        }

        // 降级路径：关键词提取（追加模式）
        var keywordSummary = ExtractRoundInfo(round, messages, assistantMessage);
        if (string.IsNullOrWhiteSpace(keywordSummary))
            return;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var memoryPath = Path.Combine(_sessionDir, "session-memory.md");
            var existingContent = "";
            if (File.Exists(memoryPath))
            {
                existingContent = await File.ReadAllTextAsync(memoryPath, cancellationToken);
            }

            var sb = new StringBuilder(existingContent);
            if (sb.Length > 0)
                sb.AppendLine();
            sb.AppendLine($"## Round {round} ({DateTime.UtcNow:HH:mm:ss})");
            sb.AppendLine(keywordSummary);

            await File.WriteAllTextAsync(memoryPath, sb.ToString(), cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 使用 LLM 生成锚定增量摘要（Anchored Summary）
    ///
    /// 流程：读取已有摘要 → 传入 previous-summary → LLM 合并新事实 → 返回完整摘要
    /// </summary>
    private async Task<string> GenerateLlmSummaryAsync(
        ILlmClient llmClient,
        string existingSummary,
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken)
    {
        try
        {
            // 构建对话文本：最近几轮
            var recentMessages = messages.TakeLast(10).ToList();
            var conversationText = new StringBuilder();
            foreach (var msg in recentMessages)
            {
                var role = msg.Role switch
                {
                    MessageRole.User => "User",
                    MessageRole.Assistant => "Assistant",
                    MessageRole.System => "System",
                    _ => msg.Role.ToString()
                };
                var content = msg.GetTextContent();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    if (content.Length > 2000)
                        content = content[..2000] + "...";
                    conversationText.AppendLine($"{role}: {content}");
                }
            }

            var promptTemplate = PromptLoader.Load("anchored-summary");
            var previousSummary = string.IsNullOrEmpty(existingSummary) ? "(none)" : existingSummary;
            var prompt = promptTemplate
                .Replace("{CONVERSATION}", conversationText.ToString())
                .Replace("{PREVIOUS_SUMMARY}", previousSummary);

            var request = new LlmRequest
            {
                Model = _options.SummaryModel,
                Messages = [Message.FromSystem(prompt)],
                Tools = [],
                ToolChoice = ToolChoiceMode.None,
                MaxTokens = _options.SummaryMaxTokens,
                Temperature = _options.SummaryTemperature
            };

            var response = await llmClient.CompleteAsync(request, cancellationToken);
            var responseText = response?.GetTextContent();

            return ExtractSummary(responseText ?? "");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SessionMemory] LLM summary failed: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// 从 LLM 响应中提取 <summary> 标签内容
    /// </summary>
    private static string ExtractSummary(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "";

        // 尝试解析 <summary>...</summary> 标签
        var match = Regex.Match(response, @"<summary>\s*(.*?)\s*</summary>", RegexOptions.Singleline);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // 如果没有标签，返回整个响应（截断）
        return response.Length > 1000 ? response[..1000] : response;
    }

    /// <summary>
    /// 提取本轮关键信息（规则方式）
    /// </summary>
    private static string ExtractRoundInfo(
        int round,
        IReadOnlyList<Message> messages,
        Message? assistantMessage)
    {
        var sb = new StringBuilder();

        // 获取最近的用户消息
        var userMessages = messages
            .Where(m => m.Role == MessageRole.User)
            .TakeLast(3)
            .ToList();

        foreach (var msg in userMessages)
        {
            var content = msg.GetTextContent();
            if (string.IsNullOrWhiteSpace(content))
                continue;

            // 提取关键信息
            var keyInfo = ExtractKeyInformation(content);
            if (!string.IsNullOrWhiteSpace(keyInfo))
            {
                sb.AppendLine(keyInfo);
            }
        }

        // 提取助手回复中的关键决策
        if (assistantMessage != null)
        {
            var assistantContent = assistantMessage.GetTextContent();
            if (!string.IsNullOrWhiteSpace(assistantContent))
            {
                var decisions = ExtractDecisions(assistantContent);
                if (!string.IsNullOrWhiteSpace(decisions))
                {
                    sb.AppendLine(decisions);
                }
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// 从用户消息中提取关键信息
    /// </summary>
    private static string ExtractKeyInformation(string content)
    {
        var sb = new StringBuilder();
        var lower = content.ToLowerInvariant();

        // 用户偏好
        if (lower.Contains("我喜欢") || lower.Contains("我偏好") || lower.Contains("i prefer") ||
            lower.Contains("不要") || lower.Contains("don't"))
        {
            sb.AppendLine($"- 用户偏好: {Truncate(content, 100)}");
        }

        // 项目信息
        if (lower.Contains("项目") || lower.Contains("project") || lower.Contains("目标") ||
            lower.Contains("goal") || lower.Contains("截止") || lower.Contains("deadline"))
        {
            sb.AppendLine($"- 项目信息: {Truncate(content, 100)}");
        }

        // 决策
        if (lower.Contains("决定") || lower.Contains("decide") || lower.Contains("选择") ||
            lower.Contains("choose") || lower.Contains("方案") || lower.Contains("approach"))
        {
            sb.AppendLine($"- 决策: {Truncate(content, 100)}");
        }

        // 问题/错误
        if (lower.Contains("错误") || lower.Contains("error") || lower.Contains("问题") ||
            lower.Contains("issue") || lower.Contains("bug"))
        {
            sb.AppendLine($"- 问题: {Truncate(content, 100)}");
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// 从助手回复中提取关键决策
    /// </summary>
    private static string ExtractDecisions(string content)
    {
        var sb = new StringBuilder();

        // 查找决策相关的模式
        var patterns = new[]
        {
            @"(?:我建议|我决定|让我们|我选择|方案是|I suggest|I recommend|let's|we should)[:\s]*(.{20,100})",
            @"(?:修改了|更新了|实现了|added|updated|implemented|fixed)[:\s]*(.{20,100})"
        };

        foreach (var pattern in patterns)
        {
            var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                sb.AppendLine($"- {Truncate(match.Value, 80)}");
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// 截断文本
    /// </summary>
    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    /// <summary>
    /// 保存会话元数据
    /// </summary>
    private async Task SaveMetadataAsync(CancellationToken cancellationToken)
    {
        var metadata = new
        {
            session_id = _sessionId,
            user_id = _userId,
            project_id = _projectId,
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        var metadataPath = Path.Combine(_sessionDir, "metadata.json");
        await File.WriteAllTextAsync(metadataPath, json, cancellationToken);
    }
}
