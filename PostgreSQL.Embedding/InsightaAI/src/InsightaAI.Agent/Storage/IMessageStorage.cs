namespace InsightaAI.Agent.Storage;

/// <summary>
/// 消息存储接口
/// </summary>
public interface IMessageStorage
{
    /// <summary>创建会话</summary>
    Task<SessionRecord> CreateSessionAsync(string model, string provider, string? title = null, string? userId = null, string? workDir = null);

    /// <summary>获取会话</summary>
    Task<SessionRecord?> GetSessionAsync(string sessionId);

    /// <summary>获取会话列表</summary>
    Task<List<SessionRecord>> GetSessionsAsync(string? userId = null, int limit = 50);

    /// <summary>获取指定工作目录下最近的会话</summary>
    Task<SessionRecord?> GetLastSessionForWorkDirAsync(string workDir);

    /// <summary>更新会话</summary>
    Task UpdateSessionAsync(SessionRecord session);

    /// <summary>删除会话及其消息</summary>
    Task DeleteSessionAsync(string sessionId);

    /// <summary>添加消息</summary>
    Task AddMessageAsync(string sessionId, MessageRecord message);

    /// <summary>获取消息列表</summary>
    Task<List<MessageRecord>> GetMessagesAsync(string sessionId, int? limit = null);

    /// <summary>清空会话消息</summary>
    Task ClearMessagesAsync(string sessionId);
}
