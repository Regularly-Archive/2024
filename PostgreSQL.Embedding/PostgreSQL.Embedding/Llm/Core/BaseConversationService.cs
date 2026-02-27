using Google.Protobuf.WellKnownTypes;
using LLama.Batched;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Core.ChatHistory.Models;
using PostgreSQL.Embedding.Llm.Core.ChatHistory.Services;
using System.Text;

namespace PostgreSQL.Embedding.Llm.Core
{
    public class BaseConversationService
    {
        private readonly Kernel _kernel;
        private readonly IChatHistoriesService _chatHistoriesService;
        private readonly ChatHistoryManager _chatHistoryManager;

        public BaseConversationService(
            Kernel kernel,
            IChatHistoriesService chatHistoriesService,
            IServiceProvider serviceProvider,
            ChatHistoryManager? chatHistoryManager = null)
        {
            _kernel = kernel;
            _chatHistoriesService = chatHistoriesService;
            _chatHistoryManager = chatHistoryManager ?? new ChatHistoryManager(
                new ChatHistoryConfig()
                {
                    ActiveRounds = 5,
                    BufferRounds = 3
                },
                _kernel,
                _chatHistoriesService,
                serviceProvider.GetRequiredService<IRepository<AppConversationState>>()
             );
        }

        /// <summary>
        /// 获取压缩后的上下文（自动处理加载/压缩/保存）
        /// </summary>
        public async Task<string> GetOrCreateContextAsync(long appId, string conversationId)
        {
            var context = await _chatHistoryManager.GetOrCreateContextAsync(appId, conversationId);
            var stringBuilder = new StringBuilder();

            foreach (var (role, content) in context)
            {
                // 压缩块直接输出，不用包裹 message 标签
                if (role == "system")
                {
                    stringBuilder.AppendLine(content);
                }
                else
                {
                    stringBuilder.AppendLine($"<message role=\"{role}\">{content}</message>");
                }
            }

            return stringBuilder.ToString();
        }

        public async Task<string> GetHistoricalMessagesAsync(
            long appId,
            string conversationId,
            int maxMessageRounds
        )
        {
            var stringBuilder = new StringBuilder();

            var chatMessages = await _chatHistoriesService.GetConversationMessagesAsync(appId, conversationId);
            chatMessages = chatMessages.SkipLast(1).ToList();

            if (chatMessages.Count >= maxMessageRounds * 2)
            {
                var totalCount = chatMessages.Count;

                var skipedMessages = chatMessages.Take(totalCount - maxMessageRounds * 2);
                var summaryFunction = _kernel.Plugins.GetFunction("ConversationSummaryPlugin", "SummarizeConversation");
                if (summaryFunction != null)
                {
                    var skipedMessageContent = string.Join("\r\n", skipedMessages.Select(s => s.Content));
                    var summaryInput = $"请使用中文对下面的内容进行归纳和总结: {skipedMessageContent}";
                    var functionResult = await _kernel.InvokeAsync(summaryFunction, new() { ["input"] = summaryInput });

                    var summarized = functionResult.GetValue<string>().Replace("END SUMMARY", "").Trim();
                    stringBuilder.AppendLine($"<message role=\"system\">{summarized}</message>");
                }

                chatMessages = chatMessages.Skip(totalCount - maxMessageRounds * 2).Take(maxMessageRounds * 2).ToList();
            }


            foreach (var chatMessage in chatMessages)
            {
                var roleName = chatMessage.IsUserMessage ? "user" : "assistant";
                stringBuilder.AppendLine($"<message role=\"{roleName}\">{chatMessage.Content}</message>");
            }

            return stringBuilder.ToString();
        }


        public async Task<string> SearchHistoricalMessagesAsync(long appId, string conversationId, string query, int maxMessageRounds)
        {
            var stringBuilder = new StringBuilder();
            var chatMessages = await _chatHistoriesService.SearchConversationMessagesAsync(appId, conversationId, query, 0);

            if (chatMessages.Count >= maxMessageRounds * 2)
            {
                var totalCount = chatMessages.Count;

                var skipedMessages = chatMessages.Take(totalCount - maxMessageRounds * 2);
                var summaryFunction = _kernel.Plugins.GetFunction("ConversationSummaryPlugin", "SummarizeConversation");
                if (summaryFunction != null)
                {
                    var skipedMessageContent = string.Join("\r\n", skipedMessages.Select(s => s.Content));
                    var summaryInput = $"请使用中文对下面的内容进行归纳和总结: {skipedMessageContent}";
                    var functionResult = await _kernel.InvokeAsync(summaryFunction, new() { ["input"] = summaryInput });

                    var summarized = functionResult.GetValue<string>().Replace("END SUMMARY", "").Trim();
                    stringBuilder.AppendLine($"<message role=\"system\">{summarized}</message>");
                }

                chatMessages = chatMessages.Skip(totalCount - maxMessageRounds * 2).Take(maxMessageRounds * 2).ToList();
            }


            foreach (var chatMessage in chatMessages)
            {
                var roleName = chatMessage.IsUserMessage ? "user" : "assistant";
                stringBuilder.AppendLine($"<message role=\"{roleName}\">{chatMessage.Content}</message>");
            }

            return stringBuilder.ToString();
        }

        public async Task<Microsoft.SemanticKernel.ChatCompletion.ChatHistory> GetChatHistoryAsync(
            long appId,
            string conversationId,
            int maxMessageRounds
        )
        {
            var chatHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();

            var chatMessages = await _chatHistoriesService.GetConversationMessagesAsync(appId, conversationId);
            chatMessages = chatMessages.SkipLast(1).ToList();

            if (chatMessages.Count >= maxMessageRounds * 2)
            {
                var totalCount = chatMessages.Count;

                var skipedMessages = chatMessages.Take(totalCount - maxMessageRounds * 2);
                var summaryFunction = _kernel.Plugins.GetFunction("ConversationSummaryPlugin", "SummarizeConversation");
                if (summaryFunction != null)
                {
                    var skipedMessageContent = string.Join("\r\n", skipedMessages.Select(s => s.Content));
                    var summaryInput = $"请使用中文对下面的内容进行归纳和总结: {skipedMessageContent}";
                    var functionResult = await _kernel.InvokeAsync(summaryFunction, new() { ["input"] = summaryInput });

                    var summarized = functionResult.GetValue<string>().Replace("END SUMMARY", "").Trim();
                    chatHistory.AddAssistantMessage($"The is a summary of previous messages: {summarized}.");
                }

                chatMessages = chatMessages.Skip(totalCount - maxMessageRounds * 2).Take(maxMessageRounds * 2).ToList();
            }

            foreach (var chatMessage in chatMessages)
            {
                var authorRole = chatMessage.IsUserMessage ? AuthorRole.User : AuthorRole.Assistant;
                chatHistory.AddMessage(authorRole, chatMessage.Content, Encoding.UTF8);
            }

            return chatHistory;
        }

        public async Task<string> GenerateConversationTitle(string input)
        {
            var functionResult = await _kernel.InvokePromptAsync(@$"请使用简洁、概括性的文字描述用户意图，不超过10个字: {input}").ConfigureAwait(false);
            return functionResult.GetValue<string>();
        }

        public async Task<string> GetChatMessagesAsync(long appId, string conversationId, IChatHistoryReducer chatHistoryReducer)
        {
            var stringBuilder = new StringBuilder();

            var chatMessages = await _chatHistoriesService.GetConversationMessagesAsync(appId, conversationId);
            chatMessages = chatMessages.SkipLast(2).ToList();
            if (chatMessages.Count == 0) return string.Empty;

            var chatHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();
            foreach (var chatMessage in chatMessages)
            {
                var authorRole = chatMessage.IsUserMessage ? AuthorRole.User : AuthorRole.Assistant;
                chatHistory.AddMessage(authorRole, chatMessage.Content, Encoding.UTF8);
            }

            var reducedMessages = await chatHistoryReducer.ReduceAsync(chatHistory);
            if (reducedMessages == null) reducedMessages = chatHistory;

            foreach (var reducedMessage in reducedMessages)
            {
                stringBuilder.AppendLine($"<message role=\"{reducedMessage.Role.ToString()}\">{reducedMessage.Content}</message>");
            }

            return stringBuilder.ToString();
        }
    }
}
