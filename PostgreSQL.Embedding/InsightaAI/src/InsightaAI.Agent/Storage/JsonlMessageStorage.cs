using System.Text.Encodings.Web;
using System.Text.Json;

namespace InsightaAI.Agent.Storage;

/// <summary>
/// JSONL 格式消息存储 - 适合 CLI 单用户场景
///
/// 目录结构:
///   basePath/
///     sessions.jsonl          # 会话索引
///     {sessionId}/
///       messages.jsonl        # 消息记录
/// </summary>
public class JsonlMessageStorage : IMessageStorage
{
    private readonly string _basePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public JsonlMessageStorage(string? basePath = null)
    {
        _basePath = basePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".insightaai");
        Directory.CreateDirectory(_basePath);
    }

    public async Task<SessionRecord> CreateSessionAsync(string model, string provider, string? title = null, string? userId = null)
    {
        var session = new SessionRecord
        {
            Model = model,
            Provider = provider,
            Title = title,
            UserId = userId
        };

        await _lock.WaitAsync();
        try
        {
            var sessionsFile = GetSessionsFilePath();
            await File.AppendAllTextAsync(sessionsFile,
                JsonSerializer.Serialize(session, JsonOptions) + Environment.NewLine);
        }
        finally
        {
            _lock.Release();
        }

        // 创建会话目录
        var sessionDir = GetSessionDir(session.Id);
        Directory.CreateDirectory(sessionDir);

        return session;
    }

    public async Task<SessionRecord?> GetSessionAsync(string sessionId)
    {
        var sessions = await ReadAllSessionsAsync();
        return sessions.FirstOrDefault(s => s.Id == sessionId);
    }

    public async Task<List<SessionRecord>> GetSessionsAsync(string? userId = null, int limit = 50)
    {
        var sessions = await ReadAllSessionsAsync();

        if (!string.IsNullOrEmpty(userId))
        {
            sessions = sessions.Where(s => s.UserId == userId).ToList();
        }

        return sessions
            .OrderByDescending(s => s.UpdatedAt)
            .Take(limit)
            .ToList();
    }

    public async Task UpdateSessionAsync(SessionRecord session)
    {
        await _lock.WaitAsync();
        try
        {
            var sessions = await ReadAllSessionsAsync();
            var index = sessions.FindIndex(s => s.Id == session.Id);

            if (index >= 0)
            {
                sessions[index] = session;
                await WriteAllSessionsAsync(sessions);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        await _lock.WaitAsync();
        try
        {
            // 从索引中移除
            var sessions = await ReadAllSessionsAsync();
            sessions = sessions.Where(s => s.Id != sessionId).ToList();
            await WriteAllSessionsAsync(sessions);
        }
        finally
        {
            _lock.Release();
        }

        // 删除会话目录（在锁外执行，因为是独立操作）
        var sessionDir = GetSessionDir(sessionId);
        if (Directory.Exists(sessionDir))
        {
            Directory.Delete(sessionDir, true);
        }
    }

    public async Task AddMessageAsync(string sessionId, MessageRecord message)
    {
        var messagesFile = GetMessagesFilePath(sessionId);
        await _lock.WaitAsync();
        try
        {
            // 写入消息
            await File.AppendAllTextAsync(messagesFile,
                JsonSerializer.Serialize(message, JsonOptions) + Environment.NewLine);

            // 更新会话消息计数（在同一锁内完成）
            var sessions = await ReadAllSessionsAsync();
            var session = sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session != null)
            {
                session.MessageCount++;
                session.UpdatedAt = DateTime.UtcNow;
                await WriteAllSessionsAsync(sessions);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<MessageRecord>> GetMessagesAsync(string sessionId, int? limit = null)
    {
        var messagesFile = GetMessagesFilePath(sessionId);
        if (!File.Exists(messagesFile))
            return [];

        var lines = await File.ReadAllLinesAsync(messagesFile);
        var messages = lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<MessageRecord>(l)!)
            .ToList();

        if (limit.HasValue)
        {
            messages = messages.TakeLast(limit.Value).ToList();
        }

        return messages;
    }

    public async Task ClearMessagesAsync(string sessionId)
    {
        var messagesFile = GetMessagesFilePath(sessionId);
        if (File.Exists(messagesFile))
        {
            await File.WriteAllTextAsync(messagesFile, "");
        }

        // 重置会话消息计数
        var session = await GetSessionAsync(sessionId);
        if (session != null)
        {
            session.MessageCount = 0;
            session.UpdatedAt = DateTime.UtcNow;
            await UpdateSessionAsync(session);
        }
    }

    // --- 私有方法 ---

    private string GetSessionsFilePath() => Path.Combine(_basePath, "sessions.jsonl");

    private string GetSessionDir(string sessionId) => Path.Combine(_basePath, sessionId);

    private string GetMessagesFilePath(string sessionId) => Path.Combine(GetSessionDir(sessionId), "messages.jsonl");

    private async Task<List<SessionRecord>> ReadAllSessionsAsync()
    {
        var sessionsFile = GetSessionsFilePath();
        if (!File.Exists(sessionsFile))
            return [];

        var lines = await File.ReadAllLinesAsync(sessionsFile);
        return lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<SessionRecord>(l)!)
            .ToList();
    }

    private async Task WriteAllSessionsAsync(List<SessionRecord> sessions)
    {
        var sessionsFile = GetSessionsFilePath();
        var lines = sessions.Select(s => JsonSerializer.Serialize(s, JsonOptions));
        await File.WriteAllLinesAsync(sessionsFile, lines);
    }
}
