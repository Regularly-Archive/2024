using PostgreSQL.Embedding.Domain.Entities;

namespace PostgreSQL.Embedding.Llm.Abstractions
{
    public interface IChatHistoriesService
    {
        Task<long> AddUserMessageAsync(long appId, string conversationId, string content);
        Task<long> AddSystemMessageAsync(long appId, string conversationId, string content);
        Task UpdateSystemMessageAsync(long messageId, string content);
        Task UpdateSystemMessageAsync(long messageId, Action<ChatMessage> action);
        Task AddConversationAsync(long appId, string conversationId, string conversationTitle);
        Task<List<AppConversation>> GetAppConversationsAsync(long appId);
        Task<List<ChatMessage>> GetConversationMessagesAsync(long appId, string conversationId);
        Task DeleteConversationAsync(long appId, string conversationId);
        Task UpdateConversationAsync(long appId, string conversationId, string conversationTitle);
        Task DeleteConversationMessageAsync(long messageId);
        Task<List<ChatMessage>> SearchConversationMessagesAsync(long appId, string conversationId, string query, double? minRelevance = 0.5, int? limit = 5);
        Task<AppConversation> GetAppConversationAsync(long appId, string conversationId);
    }
}
