using LLama.Common;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IChatHistoryService
    {
        Task<long> AddUserMessage(long appId, string conversationId, string content);
        Task<long> AddSystemMessage(long appId, string conversationId, string content);
        Task AddConversation(long appId, string conversationId, string conversationName);
        Task<List<AppConversation>> GetAppConversations(long appId);
        Task<List<ChatMessage>> GetConversationMessages(long appId, string conversationId);
        Task DeleteConversation(long appId, string conversationId);
        Task UpdateConversation(long appId, string conversationId, string summary);
        Task DeleteConversationMessage(long messageId);
    }
}
