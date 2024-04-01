using LLama.Common;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IChatHistoryService
    {
        Task AddUserMessage(long appId, string conversationId, string content);
        Task AddSystemMessage(long appId, string conversationId, string content);
        Task AddConversation(long appId, string conversationId, string conversationName);
        Task<List<AppConversation>> GetAppConversations(long appId);
        Task<List<ChatMessage>> GetConversationMessages(long appId, string conversationId);
        Task DeleteConversation(long appId, string conversationId);
        Task UpdateConversation(AppConversation conversation);
    }
}
