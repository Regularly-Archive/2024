using System.Text.Json;
using SqlSugar;

namespace InsightaAI.Agent.Storage;

/// <summary>
/// PostgreSQL 消息存储 - 适合 Web 多用户场景
///
/// 表结构:
///   session_records     - 会话表
///   message_records     - 消息表
/// </summary>
public class PostgresMessageStorage : IMessageStorage
{
    private readonly ISqlSugarClient _db;

    public PostgresMessageStorage(string connectionString)
    {
        _db = new SqlSugarClient(new ConnectionConfig
        {
            DbType = DbType.PostgreSQL,
            ConnectionString = connectionString,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        });

        // 建表（如果不存在）
        InitTables();
    }

    /// <summary>
    /// 注入已有的 SqlSugarClient（用于 DI 场景）
    /// </summary>
    public PostgresMessageStorage(ISqlSugarClient db)
    {
        _db = db;
    }

    public async Task<SessionRecord> CreateSessionAsync(string model, string provider, string? title = null, string? userId = null, string? workDir = null)
    {
        var session = new SessionRecord
        {
            Model = model,
            Provider = provider,
            Title = title,
            UserId = userId,
            WorkDir = workDir
        };

        await _db.Insertable(session).ExecuteCommandAsync();
        return session;
    }

    public async Task<SessionRecord?> GetSessionAsync(string sessionId)
    {
        return await _db.Queryable<SessionRecord>()
            .Where(s => s.Id == sessionId)
            .FirstAsync();
    }

    public async Task<List<SessionRecord>> GetSessionsAsync(string? userId = null, int limit = 50)
    {
        var query = _db.Queryable<SessionRecord>();

        if (!string.IsNullOrEmpty(userId))
        {
            query = query.Where(s => s.UserId == userId);
        }

        return await query
            .OrderByDescending(s => s.UpdatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<SessionRecord?> GetLastSessionForWorkDirAsync(string workDir)
    {
        return await _db.Queryable<SessionRecord>()
            .Where(s => s.WorkDir == workDir)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstAsync();
    }

    public async Task UpdateSessionAsync(SessionRecord session)
    {
        session.UpdatedAt = DateTime.UtcNow;
        await _db.Updateable(session)
            .IgnoreColumns(s => new { s.CreatedAt })
            .ExecuteCommandAsync();
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        await _db.Deleteable<MessageRecord>()
            .Where(m => m.SessionId == sessionId)
            .ExecuteCommandAsync();

        await _db.Deleteable<SessionRecord>()
            .Where(s => s.Id == sessionId)
            .ExecuteCommandAsync();
    }

    public async Task AddMessageAsync(string sessionId, MessageRecord message)
    {
        message.SessionId = sessionId;
        await _db.Insertable(message).ExecuteCommandAsync();

        // 更新会话消息计数
        await _db.Updateable<SessionRecord>()
            .SetColumns(s => s.MessageCount, s => s.MessageCount + 1)
            .SetColumns(s => s.UpdatedAt, DateTime.UtcNow)
            .Where(s => s.Id == sessionId)
            .ExecuteCommandAsync();
    }

    public async Task<List<MessageRecord>> GetMessagesAsync(string sessionId, int? limit = null)
    {
        var query = _db.Queryable<MessageRecord>()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt);

        if (limit.HasValue)
        {
            // 获取最后 N 条
            return await query
                .Skip(0)
                .Take(limit.Value)
                .ToListAsync();
        }

        return await query.ToListAsync();
    }

    public async Task ClearMessagesAsync(string sessionId)
    {
        await _db.Deleteable<MessageRecord>()
            .Where(m => m.SessionId == sessionId)
            .ExecuteCommandAsync();

        await _db.Updateable<SessionRecord>()
            .SetColumns(s => s.MessageCount, 0)
            .SetColumns(s => s.UpdatedAt, DateTime.UtcNow)
            .Where(s => s.Id == sessionId)
            .ExecuteCommandAsync();
    }

    private void InitTables()
    {
        _db.CodeFirst.InitTables<SessionRecord>();
        _db.CodeFirst.InitTables<MessageRecord>();
    }
}
