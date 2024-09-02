using LLama.Common;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IChatHistoriesService
    {
        Task<long> AddUserMessageAsync(long appId, string conversationId, string content);
        Task<long> AddSystemMessageAsync(long appId, string conversationId, string content);
        Task AddConversationAsync(long appId, string conversationId, string conversationName);
        Task<List<AppConversation>> GetAppConversationsAsync(long appId);
        Task<List<ChatMessage>> GetConversationMessagesAsync(long appId, string conversationId);
        Task DeleteConversationAsync(long appId, string conversationId);
        Task UpdateConversationAsync(long appId, string conversationId, string summary);
        Task DeleteConversationMessageAsync(long messageId);
        Task<List<ChatMessage>> SearchConversationMessagesAsync(long appId, string conversationId, string query, double? minRelevance = 0.5, int? limit = 5);
    }
}
