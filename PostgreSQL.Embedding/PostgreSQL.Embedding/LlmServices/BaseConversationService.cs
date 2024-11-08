﻿using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Text;

namespace PostgreSQL.Embedding.LlmServices
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
                foreach (var chatMessage in chatMessages)
                {
                    var roleName = chatMessage.IsUserMessage ? "user" : "assistant";
                    stringBuilder.AppendLine($"{roleName}: {chatMessage.Content}");
                }

                var chatHistories = stringBuilder.ToString();
                var summaryFunction = _kernel.Plugins.GetFunction("ConversationSummaryPlugin", "SummarizeConversation");
                if (summaryFunction == null) return string.Empty;

                var summaryInput = $"请使用中文对下面的内容进行归纳和总结: {chatHistories}";
                var functionResult = await _kernel.InvokeAsync(summaryFunction, new() { ["input"] = summaryInput });

                var summarized = functionResult.GetValue<string>().Replace("END SUMMARY", "").Trim();
                return $"<message role=\"system\">历史聊天摘要：{summarized}</message>";
            }
            else
            {
                foreach (var chatMessage in chatMessages)
                {
                    var roleName = chatMessage.IsUserMessage ? "user" : "assistant";
                    stringBuilder.AppendLine($"<message role=\"{roleName}\">{chatMessage.Content}</message>");
                }

                return stringBuilder.ToString();
            }
        }

        // Todo: maxMessageRounds 从 App 上取
        public async Task<string> SearchHistoricalMessagesAsync(long appId, string conversationId, string query, int maxMessageRounds)
        {
            var stringBuilder = new StringBuilder();
            var chatMessages = await _chatHistoriesService.SearchConversationMessagesAsync(appId, conversationId, query, 0);
            if (chatMessages.Count >= maxMessageRounds * 2)
            {
                foreach (var chatMessage in chatMessages)
                {
                    var roleName = chatMessage.IsUserMessage ? "user" : "assistant";
                    stringBuilder.AppendLine($"{roleName}: {chatMessage.Content}");
                }

                var chatHistories = stringBuilder.ToString();
                var summaryFunction = _kernel.Plugins.GetFunction("ConversationSummaryPlugin", "SummarizeConversation");
                if (summaryFunction == null) return string.Empty;

                var summaryInput = $"请使用中文对下面的内容进行归纳和总结: {chatHistories}";
                var functionResult = await _kernel.InvokeAsync(summaryFunction, new() { ["input"] = summaryInput });

                var summarized = functionResult.GetValue<string>().Replace("END SUMMARY", "").Trim();
                return $"<message role=\"system\">历史聊天摘要：{summarized}</message>";
            }
            else
            {
                foreach (var chatMessage in chatMessages)
                {
                    var roleName = chatMessage.IsUserMessage ? "user" : "assistant";
                    stringBuilder.AppendLine($"<message role=\"{roleName}\">{chatMessage.Content}</message>");
                }

                return stringBuilder.ToString();
            }
        }
    }
}
