using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.LlmServices;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "对话历史检索插件，支持获取当前对话内容或者摘要")]
    public class ConversationHistoriesPlugin
    {
        private readonly IServiceProvider _serviceProvider;
        public ConversationHistoriesPlugin(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [KernelFunction]
        [Description("获取指定应用及会话的历史聊天信息")]
        public async Task<string> GetHistoricalMessages([Description("当前应用ID")] long appId, [Description("当前会话ID")] string conversationId, Kernel kernel)
        {
            using var serviceProviderScope = _serviceProvider.CreateScope();
            var serviceProvider = serviceProviderScope.ServiceProvider;
            var chatHistoriesService = serviceProvider.GetService<IChatHistoriesService>();
            var baseConversationService = new BaseConversationService(kernel, chatHistoriesService);
            var historicalMessages = await baseConversationService.GetHistoricalMessages(appId, conversationId,50);
            return historicalMessages;
        }
    }
}
