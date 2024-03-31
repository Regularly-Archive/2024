using LLama.Common;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IChatHistoryService
    {
        Task AddUserMessage(long appId, string conversationId, string content);
        Task AddSystemMessage(long appId, string conversationId, string content);
        Task<List<ChatHistory>> GetChatHistories(long appId);
        Task<List<ChatMessage>> GetHistoricalMessages(long appId, string conversationId);
    }
}
