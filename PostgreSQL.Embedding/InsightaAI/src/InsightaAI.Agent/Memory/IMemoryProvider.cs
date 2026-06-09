namespace InsightaAI.Agent.Memory;

/// <summary>
/// 记忆存储提供者接口
/// </summary>
public interface IMemoryProvider
{
    /// <summary>
    /// 保存记忆
    /// </summary>
    Task SaveMemoryAsync(MemoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取记忆
    /// </summary>
    Task<MemoryEntry?> GetMemoryAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 搜索记忆（关键词匹配）
    /// </summary>
    Task<List<MemoryEntry>> SearchMemoriesAsync(
        string userId,
        string query,
        MemorySearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户的所有记忆（分页）
    /// </summary>
    Task<List<MemoryEntry>> ListMemoriesAsync(
        string userId,
        MemoryType? type = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新记忆
    /// </summary>
    Task UpdateMemoryAsync(MemoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除记忆
    /// </summary>
    Task DeleteMemoryAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 MEMORY.md 索引内容（用于注入 System Prompt）
    /// </summary>
    Task<string> GetMemoryIndexAsync(
        string userId,
        string? projectId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户画像
    /// </summary>
    Task<UserProfile?> GetUserProfileAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存用户画像
    /// </summary>
    Task SaveUserProfileAsync(UserProfile profile, CancellationToken cancellationToken = default);
}
