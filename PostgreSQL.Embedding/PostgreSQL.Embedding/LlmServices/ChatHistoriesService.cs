using LLama.Common;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Text;

namespace PostgreSQL.Embedding.LlmServices
{
    public class ChatHistoriesService : IChatHistoriesService
    {
        private IRepository<ChatMessage> _chatMessageRepository;
        private readonly IRepository<AppConversation> _appConversationRepository;
        public ChatHistoriesService(IRepository<ChatMessage> chatMessageRepository, IRepository<AppConversation> appConversationRepository)
        {
            _chatMessageRepository = chatMessageRepository;
            _appConversationRepository = appConversationRepository;
        }

        public async Task<long> AddSystemMessage(long appId, string conversationId, string content)
        {
            var message = await _chatMessageRepository.AddAsync(new ChatMessage()
            {
                AppId = appId,
                ConversationId = conversationId,
                Content = content,
                IsUserMessage = false
            });

            return message.Id;
        }

        public async Task<long> AddUserMessage(long appId, string conversationId, string content)
        {
            var message = await _chatMessageRepository.AddAsync(new ChatMessage()
            {
                AppId = appId,
                ConversationId = conversationId,
                Content = content,
                IsUserMessage = true
            });

            return message.Id;
        }

        public Task<List<AppConversation>> GetAppConversations(long appId)
        {
            return _appConversationRepository.FindAsync(x => x.AppId == appId);
        }

        public async Task<List<ChatMessage>> GetConversationMessages(long appId, string conversationId)
        {
            var messages = await _chatMessageRepository.FindAsync(x => x.AppId == appId && x.ConversationId == conversationId);
            messages = messages.OrderBy(x => x.CreatedAt).ToList();
            return messages;
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

        public async Task UpdateConversation(long appId, string conversationId, string summary)
        {
            var conversation = await _appConversationRepository.SingleOrDefaultAsync(x => x.AppId == appId && x.ConversationId == conversationId);
            if (conversation == null) return;

            conversation.Summary = summary;
            await _appConversationRepository.UpdateAsync(conversation);
        }

        public Task DeleteConversationMessage(long messageId)
        {
            return _chatMessageRepository.DeleteAsync(messageId);
        }


    }
}
