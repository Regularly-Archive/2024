using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.DataAccess.Entities;
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

        public async Task<string> GetHistoricalMessages(long appId, string conversationId, int maxMessageRounds)
        {
            var stringBuilder = new StringBuilder();
            var chatMessages = await _chatHistoriesService.GetConversationMessages(appId, conversationId);
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
                var summarized = await _kernel.InvokeAsync(summaryFunction, new() { ["input"] = summaryInput });

                return $"<message role=\"system\">历史聊天摘要：{summarized.GetValue<string>()}</message>";
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
