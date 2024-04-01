using LLama.Common;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;

namespace PostgreSQL.Embedding.LlmServices
{
    public class ChatHistoryService : IChatHistoryService
    {
        private IRepository<ChatMessage> _chatMessageRepository;
        private readonly IRepository<AppConversation> _appConversationRepository;
        public ChatHistoryService(IRepository<ChatMessage> chatMessageRepository, IRepository<AppConversation> appConversationRepository)
        {
            _chatMessageRepository = chatMessageRepository;
            _appConversationRepository = appConversationRepository;
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

        public Task<List<AppConversation>> GetAppConversations(long appId)
        {
            return _appConversationRepository.FindAsync(x => x.AppId == appId);
        }

        public Task<List<ChatMessage>> GetConversationMessages(long appId, string conversationId)
        {
            return _chatMessageRepository.FindAsync(x => x.AppId == appId && x.ConversationId == conversationId);
        }

        public async Task AddConversation(long appId, string conversationId, string conversationName)
        {
            var conversation = await _appConversationRepository.SingleOrDefaultAsync(x => x.AppId == appId && x.ConversationId == conversationId);
            if (conversation != null) return;

            await _appConversationRepository.AddAsync(new AppConversation()
            {
                AppId = appId,
                ConversationId = conversationId,
                Summary = conversationName,
            });
        }

        public async Task DeleteConversation(long appId, string conversationId)
        {
            await _appConversationRepository.DeleteAsync(x => x.AppId == appId && x.ConversationId == conversationId);
            await _chatMessageRepository.DeleteAsync(x => x.AppId == appId && x.ConversationId == conversationId);
        }

        public Task UpdateConversation(AppConversation conversation)
        {
            return _appConversationRepository.UpdateAsync(conversation);
        }
    }
}
