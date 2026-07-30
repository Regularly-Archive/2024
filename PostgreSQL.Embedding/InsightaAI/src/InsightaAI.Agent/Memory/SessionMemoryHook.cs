using System.Text.Json;
using InsightaAI.Agent.Context.Summary;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Memory;

/// <summary>
/// 会话记忆元数据
/// </summary>
public sealed record SessionMemoryMetadata
{
    /// <summary>会话 ID</summary>
    public string SessionId { get; init; } = "";

    /// <summary>用户 ID</summary>
    public string UserId { get; init; } = "";

    /// <summary>项目 ID</summary>
    public string? ProjectId { get; init; }

    /// <summary>会话创建时间</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>最后更新时间</summary>
    public DateTime UpdatedAt { get; init; }
}

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
    public TimeSpan SummaryInterval { get; init; } = TimeSpan.FromMinutes(5);

}

/// <summary>
/// 会话记忆钩子 - 在每轮结束后提取短期记忆，支持 LLM 增强摘要
///
/// 存储结构：
/// ~/.insightai/memory/sessions/{sessionId}/
/// ├── MEMORY.md    # 会话级记忆（短期）
/// └── metadata.json        # 会话元数据
/// </summary>
public sealed class SessionMemoryHook : IAgentEventHook
{
    private readonly string _sessionId;
    private readonly string _userId;
    private readonly string? _projectId;
    private readonly string _sessionDir;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // LLM 增强配置
    private readonly SessionMemoryOptions _options;
    private readonly ISummaryService? _summaryService;

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
        SessionMemoryOptions? options = null,
        ISummaryService? summaryService = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentException.ThrowIfNullOrEmpty(userId);

        _options = options ?? new SessionMemoryOptions();
        _summaryService = summaryService;

        _sessionId = sessionId;
        _userId = userId;
        _projectId = projectId;

        // 会话记忆目录
        var memoryBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".insighta", "memories", "sessions", sessionId);
        _sessionDir = memoryBase;
        Directory.CreateDirectory(_sessionDir);
    }

    /// <summary>
    /// 每轮结束后触发，异步提取记忆
    /// </summary>
    public Task OnAgentRoundEndedAsync(
        AgentEventHookContext context,
        IReadOnlyList<Message> messages,
        Message? assistantMessage,
        CancellationToken cancellationToken = default)
    {
        if (context.GetEvent<AgentRoundEndEvent>().Round < _options.MinRoundsBeforeLlm)
            return Task.CompletedTask;

        ExtractMemoryInBackground(context, messages);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 会话结束时触发
    /// </summary>
    public Task OnAgentTurnEndedAsync(
        AgentEventHookContext context,
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken = default)
    {
        ExtractMemoryInBackground(context, messages);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 在后台执行记忆提取（创建快照避免并发问题）
    /// </summary>
    private void ExtractMemoryInBackground(AgentEventHookContext context, IReadOnlyList<Message> messages)
    {
        var messagesSnapshot = messages.ToList();

        _ = Task.Run(async () =>
        {
            try
            {
                await ExtractAndSaveMemoryAsync(context, messagesSnapshot, CancellationToken.None);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SessionMemoryHook] 后台记忆提取失败: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 获取会话级记忆内容（用于 L2 压缩）
    /// </summary>
    public async Task<string> GetSessionMemoryAsync(CancellationToken cancellationToken = default)
    {
        var memoryPath = Path.Combine(_sessionDir, "MEMORY.md");
        if (!File.Exists(memoryPath))
            return "";

        return await File.ReadAllTextAsync(memoryPath, cancellationToken);
    }

    /// <summary>
    /// 提取并保存会话记忆
    /// </summary>
    private async Task ExtractAndSaveMemoryAsync(
        AgentEventHookContext context,
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken)
    {
        // Step 0: 检查是否满足 LLM 摘要条件
        if (_options.EnableLlmSummary && _summaryService != null)
        {
            // Step 0.1: 检查时间间隔，避免频繁调用 LLM
            var metadata = await LoadMetadataAsync(cancellationToken);
            if (metadata != null)
            {
                var elapsed = DateTime.UtcNow - metadata.UpdatedAt;
                if (elapsed.TotalMinutes < _options.SummaryInterval.TotalMinutes)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[SessionMemory] Skip LLM summary, elapsed {elapsed.TotalMinutes:F1}min < {_options.SummaryInterval}min");
                    return;
                }
            }

            // Step 1: 读取已有摘要
            var previousSummary = await GetSessionMemoryAsync(cancellationToken);

            // Step 2: 使用 LLM 锚定增量摘要（读取旧摘要 → 合并新事实 → 替换文件）
            var result = await _summaryService.UpdateAsync(previousSummary, messages.TakeLast(10).ToList(), cancellationToken);
            var mergedSummary = result.Success ? result.Summary : null;

            if (!string.IsNullOrWhiteSpace(mergedSummary))
            {
                // Step 3: 替换文件（不是追加）
                await _lock.WaitAsync(cancellationToken);
                try
                {
                    var memoryPath = Path.Combine(_sessionDir, "MEMORY.md");
                    await File.WriteAllTextAsync(memoryPath, mergedSummary, cancellationToken);
                }
                finally
                {
                    _lock.Release();
                }

                // Step 4: 更新元数据（记录更新时间）
                await CreateOrUpdateMetadata(cancellationToken);
                return;
            }
        }
    }

    /// <summary>
    /// 加载会话记忆元数据（不存在则返回 null）
    /// </summary>
    private async Task<SessionMemoryMetadata?> LoadMetadataAsync(CancellationToken cancellationToken)
    {
        try
        {
            var metadataPath = Path.Combine(_sessionDir, "metadata.json");
            if (!File.Exists(metadataPath))
                return null;

            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
            return JsonSerializer.Deserialize<SessionMemoryMetadata>(json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 创建或者更新会话记忆元数据
    /// </summary>
    private async Task CreateOrUpdateMetadata(CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(_sessionDir, "metadata.json");
        var now = DateTime.UtcNow;

        SessionMemoryMetadata metadata;

        if (File.Exists(metadataPath))
        {
            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
            var existing = JsonSerializer.Deserialize<SessionMemoryMetadata>(json);

            metadata = existing! with { UpdatedAt = now };
        }
        else
        {
            metadata = new SessionMemoryMetadata
            {
                SessionId = _sessionId,
                UserId = _userId,
                ProjectId = _projectId,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, metadataJson, cancellationToken);
    }
}
