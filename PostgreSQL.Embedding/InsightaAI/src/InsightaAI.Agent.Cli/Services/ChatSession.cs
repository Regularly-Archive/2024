using InsightaAI.Agent.Storage;
using InsightaAI.LLM.Models;
using System.Text.Json;

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
    /// 添加带工具调用的助手消息
    /// </summary>
    public async Task AddAssistantWithToolCallsAsync(string? text, List<ToolCallContent> toolCalls)
    {
        var content = new List<ContentItem>();
        if (!string.IsNullOrEmpty(text))
            content.Add(new TextContent { Text = text });
        content.AddRange(toolCalls);

        var message = new MessageRecord
        {
            Role = RoleAssistant,
            Content = content
        };
        _messages.Add(message);
        await _storage.AddMessageAsync(SessionId, message);
    }

    /// <summary>
    /// 添加工具结果消息
    /// </summary>
    public async Task AddToolResultMessageAsync(string toolCallId, string toolName, string result, bool isError)
    {
        var message = new MessageRecord
        {
            Role = "tool",
            ToolCallId = toolCallId,
            ToolName = toolName,
            Content = [new ToolResultContent { ToolCallId = toolCallId, ToolName = toolName, Text = result, IsError = isError }]
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
            ToolName = message.ToolName,
            CreatedAt = message.Timestamp.UtcDateTime,
        };
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
            RoleUser => new Message { Role = MessageRole.User, Content = contentBlocks.ToArray(), Timestamp = record.CreatedAt },
            RoleAssistant => new Message { Role = MessageRole.Assistant, Content = contentBlocks.ToArray(), Timestamp = record.CreatedAt },
            "system" => new Message { Role = MessageRole.System, Content = contentBlocks.ToArray(), Timestamp = record.CreatedAt },
            "tool" => new Message
            {
                Role = MessageRole.ToolResult,
                ToolCallId = record.ToolCallId,
                ToolName = record.ToolName,
                Content = contentBlocks.ToArray(),
                Timestamp = record.CreatedAt
            },
            _ => new Message { Role = MessageRole.User, Content = contentBlocks.ToArray(), Timestamp = record.CreatedAt }
        };
    }
}
