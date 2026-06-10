using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using InsightaAI.Agent.Hooks;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Memory;

/// <summary>
/// 会话记忆钩子 - 在每轮结束后异步提取短期记忆
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

    /// <summary>
    /// 会话 ID
    /// </summary>
    public string SessionId => _sessionId;

    /// <summary>
    /// 会话记忆目录路径
    /// </summary>
    public string SessionDirectory => _sessionDir;

    public SessionMemoryHook(string sessionId, string userId, string? projectId = null)
    {
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
    /// <remarks>
    /// 实际提取工作在后台执行，此方法立即返回 Task.CompletedTask。
    /// 这样既满足接口契约，又不阻塞主流程。
    /// </remarks>
    public Task OnRoundEndAsync(
        int round,
        IReadOnlyList<Message> messages,
        Message? assistantMessage,
        CancellationToken cancellationToken = default)
    {
        // 在后台执行提取，不传递 cancellationToken（调用方可能已取消）
        _ = Task.Run(async () =>
        {
            try
            {
                await ExtractAndSaveMemoryAsync(round, messages, assistantMessage, CancellationToken.None);
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
        int round,
        IReadOnlyList<Message> messages,
        Message? assistantMessage,
        CancellationToken cancellationToken)
    {
        // 提取本轮的关键信息
        var extractedInfo = ExtractRoundInfo(round, messages, assistantMessage);

        if (string.IsNullOrWhiteSpace(extractedInfo))
            return;

        // 追加到会话记忆文件
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
            sb.AppendLine(extractedInfo);

            await File.WriteAllTextAsync(memoryPath, sb.ToString(), cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 提取本轮关键信息
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
