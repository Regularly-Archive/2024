using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Core;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
    [KernelPlugin(Description = "检索和管理对话历史记录，支持获取历史消息列表和基于关键词的搜索。用于在上下文中引用之前的对话内容。", Version = "1.1")]
    public class ConversationHistoriesPlugin : BasePlugin
    {
        private readonly IServiceProvider _serviceProvider;
        public ConversationHistoriesPlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [KernelFunction]
        [Description("获取指定应用和会话的历史聊天消息列表，返回最近 N 条消息（默认为 15 条）")]
        public async Task<string> GetHistoricalMessages(
            [Description("应用 ID")] long appId,
            [Description("会话 ID")] string conversationId,
            Kernel kernel
        )
        {
            using var serviceProviderScope = _serviceProvider.CreateScope();
            var serviceProvider = serviceProviderScope.ServiceProvider;
            var chatHistoriesService = serviceProvider.GetService<IChatHistoriesService>();
            var baseConversationService = new BaseConversationService(kernel, chatHistoriesService,serviceProvider);
            var historicalMessages = await baseConversationService.GetHistoricalMessagesAsync(appId, conversationId, 15);
            return historicalMessages;
        }

        [KernelFunction]
        [Description("在指定应用和会话的历史消息中搜索包含关键词的消息，返回匹配的消息内容")]
        public async Task<string> SearchHistoricalMessages(
            [Description("应用 ID")] long appId,
            [Description("会话 ID")] string conversationId,
            [Description("搜索关键词")] string query,
            Kernel kernel
        )
        {
            using var serviceProviderScope = _serviceProvider.CreateScope();
            var serviceProvider = serviceProviderScope.ServiceProvider;
            var chatHistoriesService = serviceProvider.GetService<IChatHistoriesService>();
            var baseConversationService = new BaseConversationService(kernel, chatHistoriesService,serviceProvider);
            var historicalMessages = await baseConversationService.SearchHistoricalMessagesAsync(appId, conversationId, query, 15);
            return historicalMessages;
        }

    }
}
