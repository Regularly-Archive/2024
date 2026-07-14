using InsightaAI.Agent.Storage;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>
/// 聊天会话管理 - 封装消息存储和上下文构建
/// </summary>
public class ChatSession
{
    private readonly IMessageStorage _storage;
    private readonly List<MessageRecord> _messages = [];

    public string SessionId { get; }
    public string Model { get; }
    public string Provider { get; }
    public IReadOnlyList<MessageRecord> Messages => _messages;

    public ChatSession(IMessageStorage storage, SessionRecord session)
    {
        _storage = storage;
        SessionId = session.Id;
        Model = session.Model;
        Provider = session.Provider;
    }

    /// <summary>
    /// 加载历史消息
    /// </summary>
    public async Task LoadHistoryAsync()
    {
        var messages = await _storage.GetMessagesAsync(SessionId);
        _messages.Clear();
        _messages.AddRange(messages);
    }

    /// <summary>
    /// 获取 LLM 兼容的历史消息
    /// </summary>
    public List<Message> GetLlmHistory()
    {
        return _messages.Select(r => r.ToLlmMessage()).ToList();
    }

    /// <summary>
    /// 清空上下文
    /// </summary>
    public async Task ClearAsync()
    {
        _messages.Clear();
        await _storage.ClearMessagesAsync(SessionId);
    }

    /// <summary>
    /// 替换消息历史（用于压缩后的同步）
    /// </summary>
    public async Task ReplaceMessagesAsync(List<Message> messages)
    {
        // 清空存储
        await _storage.ClearMessagesAsync(SessionId);

        // 清空内存缓存
        _messages.Clear();

        // 转换并添加新消息
        foreach (var message in messages)
        {
            // 跳过系统消息（不需要持久化）
            if (message.Role == MessageRole.System)
                continue;

            var record = message.ToMessageRecord(SessionId);
            _messages.Add(record);
            await _storage.AddMessageAsync(SessionId, record);
        }
    }

    /// <summary>
    /// 创建新的会话
    /// </summary>
    public static async Task<ChatSession> CreateAsync(IMessageStorage storage, string model, string provider, string? workDir = null)
    {
        var session = await storage.CreateSessionAsync(model, provider, workDir: workDir);
        return new ChatSession(storage, session);
    }

    /// <summary>
    /// 加载已有会话
    /// </summary>
    public static async Task<ChatSession?> LoadAsync(IMessageStorage storage, string sessionId)
    {
        var session = await storage.GetSessionAsync(sessionId);
        if (session == null) return null;

        var chatSession = new ChatSession(storage, session);
        await chatSession.LoadHistoryAsync();
        return chatSession;
    }
}
