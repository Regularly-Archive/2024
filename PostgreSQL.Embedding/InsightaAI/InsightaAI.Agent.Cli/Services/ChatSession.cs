using System.Linq;
using System.Text.Json;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Storage;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>
/// 聊天会话管理 - 封装消息存储和上下文构建
/// </summary>
public class ChatSession
{
    private const string RoleUser = "user";
    private const string RoleAssistant = "assistant";

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
    /// 添加用户消息
    /// </summary>
    public async Task AddUserMessageAsync(string text)
    {
        var message = new MessageRecord
        {
            Role = RoleUser,
            Content = [new TextContent { Text = text }]
        };
        _messages.Add(message);
        await _storage.AddMessageAsync(SessionId, message);
    }

    /// <summary>
    /// 添加助手消息
    /// </summary>
    public async Task AddAssistantMessageAsync(string text)
    {
        var message = new MessageRecord
        {
            Role = RoleAssistant,
            Content = [new TextContent { Text = text }]
        };
        _messages.Add(message);
        await _storage.AddMessageAsync(SessionId, message);
    }

    /// <summary>
    /// 获取 LLM 兼容的历史消息
    /// </summary>
    public List<Message> GetLlmHistory()
    {
        return _messages.Select(ConvertToLlmMessage).ToList();
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

            var record = ConvertToMessageRecord(message);
            _messages.Add(record);
            await _storage.AddMessageAsync(SessionId, record);
        }
    }

    private static MessageRecord ConvertToMessageRecord(Message message)
    {
        var contentItems = new List<ContentItem>();
        foreach (var block in message.Content)
        {
            ContentItem item = block switch
            {
                TextBlock text => new TextContent { Text = text.Text },
                ImageBlock image => new ImageContent
                {
                    MediaType = image.Source.MediaType,
                    Data = image.Source.Data
                },
                ToolCallBlock toolCall => new ToolCallContent
                {
                    Id = toolCall.Id,
                    Name = toolCall.Name,
                    Arguments = toolCall.Arguments.GetRawText()
                },
                ToolResultBlock toolResult => new ToolResultContent
                {
                    ToolCallId = toolResult.ToolCallId,
                    ToolName = toolResult.ToolName,
                    Text = toolResult.Content.OfType<TextBlock>().FirstOrDefault()?.Text,
                    IsError = toolResult.IsError
                },
                ThinkingBlock thinking => new ThinkingContent { Text = thinking.Thinking },
                _ => new TextContent { Text = "" }
            };
            contentItems.Add(item);
        }

        return new MessageRecord
        {
            Role = message.Role switch
            {
                MessageRole.User => RoleUser,
                MessageRole.Assistant => RoleAssistant,
                MessageRole.System => "system",
                MessageRole.ToolResult => "tool",
                _ => RoleUser
            },
            Content = contentItems,
            ToolCallId = message.ToolCallId,
            ToolName = message.ToolName
        };
    }

    /// <summary>
    /// 创建新的会话
    /// </summary>
    public static async Task<ChatSession> CreateAsync(IMessageStorage storage, string model, string provider)
    {
        var session = await storage.CreateSessionAsync(model, provider);
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

    private static Message ConvertToLlmMessage(MessageRecord record)
    {
        var contentBlocks = record.Content.Select<ContentItem, ContentBlock>(item => item switch
        {
            TextContent text => new TextBlock { Text = text.Text },
            ImageContent image => new ImageBlock
            {
                Source = new ImageSource { MediaType = image.MediaType, Data = image.Data }
            },
            ToolCallContent toolCall => new ToolCallBlock
            {
                Id = toolCall.Id,
                Name = toolCall.Name,
                Arguments = JsonSerializer.Deserialize<JsonElement>(toolCall.Arguments)
            },
            ToolResultContent toolResult => new ToolResultBlock
            {
                ToolCallId = toolResult.ToolCallId,
                ToolName = toolResult.ToolName,
                Content = [new TextBlock { Text = toolResult.Text ?? "" }],
                IsError = toolResult.IsError
            },
            ThinkingContent thinking => new ThinkingBlock { Thinking = thinking.Text },
            _ => new TextBlock { Text = "" }
        }).ToList();

        return record.Role switch
        {
            RoleUser => new Message { Role = MessageRole.User, Content = contentBlocks.ToArray() },
            RoleAssistant => new Message { Role = MessageRole.Assistant, Content = contentBlocks.ToArray() },
            "system" => new Message { Role = MessageRole.System, Content = contentBlocks.ToArray() },
            "tool" => new Message
            {
                Role = MessageRole.ToolResult,
                ToolCallId = record.ToolCallId,
                ToolName = record.ToolName,
                Content = contentBlocks.ToArray()
            },
            _ => new Message { Role = MessageRole.User, Content = contentBlocks.ToArray() }
        };
    }
}
