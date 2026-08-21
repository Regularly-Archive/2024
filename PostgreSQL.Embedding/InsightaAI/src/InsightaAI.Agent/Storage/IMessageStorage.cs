namespace InsightaAI.Agent.Storage;

/// <summary>
/// 消息存储接口
/// </summary>
public interface IMessageStorage
{
    /// <summary>创建会话</summary>
    Task<SessionRecord> CreateSessionAsync(string model, string provider, string? title = null, string? userId = null, string? workDir = null, string? parentSessionId = null, string? parentInvocationId = null, string? invocationId = null);

    /// <summary>获取会话。供运行时内部使用，可返回子会话。</summary>
    Task<SessionRecord?> GetSessionAsync(string sessionId);

    /// <summary>获取用户可见的主会话；子 Agent 会话不会返回。</summary>
    Task<SessionRecord?> GetMainSessionAsync(string sessionId);

    /// <summary>获取用户可见的主会话列表。</summary>
    Task<List<SessionRecord>> GetSessionsAsync(string? userId = null, int offset = 0, int limit = 50);

    /// <summary>获取指定工作目录下最近的用户可见主会话。</summary>
    Task<SessionRecord?> GetLastSessionForWorkDirAsync(string workDir);

    /// <summary>更新会话</summary>
    Task UpdateSessionAsync(SessionRecord session);

    /// <summary>仅更新会话标题，避免覆盖并发更新的消息计数等字段</summary>
    Task UpdateSessionTitleAsync(string sessionId, string title);

    /// <summary>删除会话及其消息</summary>
    Task DeleteSessionAsync(string sessionId);

    /// <summary>添加消息</summary>
    Task AddMessageAsync(string sessionId, MessageRecord message);

    /// <summary>获取消息列表</summary>
    Task<List<MessageRecord>> GetMessagesAsync(string sessionId, int? limit = null);

    /// <summary>清空会话消息</summary>
    Task ClearMessagesAsync(string sessionId);
}
