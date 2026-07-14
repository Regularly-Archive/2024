using InsightaAI.LLM.Models;
using System.Text.Json;

namespace InsightaAI.Agent.Storage;

/// <summary>
/// Message (LLM) 与 MessageRecord (Storage) 之间的转换
/// </summary>
public static class MessageConverters
{
    private const string RoleUser = "user";
    private const string RoleAssistant = "assistant";

    /// <summary>
    /// 将 LLM Message 转换为存储用的 MessageRecord
    /// </summary>
    public static MessageRecord ToMessageRecord(this Message message, string? sessionId = null)
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
            SessionId = sessionId,
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
    /// 将存储用的 MessageRecord 转换为 LLM Message
    /// </summary>
    public static Message ToLlmMessage(this MessageRecord record)
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
