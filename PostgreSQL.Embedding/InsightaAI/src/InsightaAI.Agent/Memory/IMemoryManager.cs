namespace InsightaAI.Agent.Memory;

/// <summary>
/// 记忆管理器接口 - 提供高级记忆操作
/// </summary>
public interface IMemoryManager
{
    /// <summary>
    /// 保存记忆（自动分类和打标签）
    /// </summary>
    Task<MemoryEntry> SaveMemoryAsync(
        string userId,
        string content,
        MemoryType? type = null,
        List<string>? tags = null,
        string? source = null,
        string? project = null,
        MemoryActivation activation = MemoryActivation.OnDemand,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新记忆内容
    /// </summary>
    Task<bool> UpdateMemoryAsync(
        string userId,
        string memoryId,
        string? content = null,
        MemoryType? type = null,
        List<string>? tags = null,
        MemoryActivation? activation = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除记忆
    /// </summary>
    Task<bool> DeleteMemoryAsync(
        string userId,
        string memoryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 智能搜索（结合语义搜索和关键词匹配）
    /// </summary>
    Task<List<MemoryEntry>> SearchRelevantMemoriesAsync(
        string userId,
        string context,
        int maxResults = 5,
        MemoryType? type = null,
        string? projectId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates the active-memory snapshot for one user turn.</summary>
    Task<ActiveMemorySnapshot> CreateActiveMemorySnapshotAsync(
        string userId,
        string input,
        string turnId,
        string? projectId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 MEMORY.md 索引（用于注入 System Prompt）
    /// </summary>
    Task<string> GetMemoryIndexAsync(
        string userId,
        string? projectId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户上下文（合并画像和索引）
    /// </summary>
    Task<string> GetUserContextAsync(
        string userId,
        string? currentProject = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新用户画像
    /// </summary>
    Task UpdateUserProfileAsync(
        string userId,
        Dictionary<string, string> updates,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户画像
    /// </summary>
    Task<UserProfile?> GetUserProfileAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存用户画像
    /// </summary>
    Task SaveUserProfileAsync(
        UserProfile profile,
        CancellationToken cancellationToken = default);
}
