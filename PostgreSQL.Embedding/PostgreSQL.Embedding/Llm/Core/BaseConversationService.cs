using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using PostgreSQL.Embedding.Llm.Abstractions;
using System.Text;

namespace PostgreSQL.Embedding.Llm.Core
{
    public class BaseConversationService
    {
        private readonly Kernel _kernel;
        private readonly IChatHistoriesService _chatHistoriesService;

        public BaseConversationService(Kernel kernel, IChatHistoriesService chatHistoriesService)
        {
            _kernel = kernel;
            _chatHistoriesService = chatHistoriesService;
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

        public async Task<ChatHistory> GetChatHistoryAsync(
            long appId,
            string conversationId,
            int maxMessageRounds
        )
        {
            var chatHistory = new ChatHistory();

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

        public async Task<string> GenerateConversationSummary(string input)
        {
            var functionResult = await _kernel.InvokePromptAsync(
                @$"请使用简洁、概括性的文字描述用户意图，不超过10个字: 
                {input}
                ");

            return functionResult.GetValue<string>();
        }
    }
}
