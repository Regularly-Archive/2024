using LLama.Common;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;

namespace PostgreSQL.Embedding.LlmServices
{
    public class ChatHistoryService : IChatHistoryService
    {
        private IRepository<ChatMessage> _chatMessageRepository;
        public ChatHistoryService(IRepository<ChatMessage> chatMessageRepository)
        {
            _chatMessageRepository = chatMessageRepository;
        }

        public Task AddSystemMessage(long appId, string conversationId, string content)
        {
            return _chatMessageRepository.AddAsync(new ChatMessage()
            {
                AppId = appId,
                ConversationId = conversationId,
                Content = content,
                IsUserMessage = false
            });
        }

        public Task AddUserMessage(long appId, string conversationId, string content)
        {
            return _chatMessageRepository.AddAsync(new ChatMessage()
            {
                AppId = appId,
                ConversationId = conversationId,
                Content = content,
                IsUserMessage = true
            });
        }

        public Task<List<ChatHistory>> GetChatHistories(long appId)
        {
            throw new NotImplementedException();
        }

        public Task<List<ChatMessage>> GetHistoricalMessages(long appId, string conversationId)
        {
            return _chatMessageRepository.FindAsync(x => x.AppId == appId && x.ConversationId == conversationId);
        }
    }
}
