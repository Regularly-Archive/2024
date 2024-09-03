using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.LlmServices;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "对话历史检索插件，支持获取当前对话内容或者摘要")]
    public class ConversationHistoriesPlugin : BasePlugin
    {
        private readonly IServiceProvider _serviceProvider;
        public ConversationHistoriesPlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [KernelFunction]
        [Description("获取指定应用及会话的全部历史聊天信息")]
        public async Task<string> GetHistoricalMessages(
            [Description("当前应用ID")] long appId,
            [Description("当前会话ID")] string conversationId,
            Kernel kernel
        )
        {
            using var serviceProviderScope = _serviceProvider.CreateScope();
            var serviceProvider = serviceProviderScope.ServiceProvider;
            var chatHistoriesService = serviceProvider.GetService<IChatHistoriesService>();
            var baseConversationService = new BaseConversationService(kernel, chatHistoriesService);
            var historicalMessages = await baseConversationService.GetHistoricalMessagesAsync(appId, conversationId, 50);
            return historicalMessages;
        }

        [KernelFunction]
        [Description("通过关键字检索指定应用及会话的历史聊天信息")]
        public async Task<string> SearchHistoricalMessages(
            [Description("当前应用ID")] long appId,
            [Description("当前会话ID")] string conversationId,
            [Description("关键词")] string query, Kernel kernel
        )
        {
            using var serviceProviderScope = _serviceProvider.CreateScope();
            var serviceProvider = serviceProviderScope.ServiceProvider;
            var chatHistoriesService = serviceProvider.GetService<IChatHistoriesService>();
            var baseConversationService = new BaseConversationService(kernel, chatHistoriesService);
            var historicalMessages = await baseConversationService.SearchHistoricalMessagesAsync(appId, conversationId, query);
            return historicalMessages;
        }
    }
}
