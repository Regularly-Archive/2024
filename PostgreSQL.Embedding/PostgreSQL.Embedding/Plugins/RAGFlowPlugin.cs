using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Core;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "适用于 RAG 任务的插件", Enabled = true, Version = "1.0")]
    public class RAGFlowPlugin : BasePlugin
    {
        public RAGFlowPlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {

        }

        [KernelFunction]
        [Description("检索信息并生成答案")]
        private async Task<string> RetrieveAndGenerateAnswerAsync(
            [Description("应用ID")] long appId, 
            [Description("会话ID")] string conversationId, 
            [Description("用户输入")] string question,
            [Description("允许联网搜索")] bool enableWebSearch,
            Kernel kernel
        )
        {
            var memoryService = _serviceProvider.GetService<IMemoryService>();
            var chatHistoryService = _serviceProvider.GetService<IChatHistoriesService>();

            var ragFlowService = new RAGFlowService(kernel, _serviceProvider, memoryService, chatHistoryService);
            var citations = await ragFlowService.GenerateCitationsAsync(appId, question, enableWebSearch);
            var answer = await ragFlowService.GenerateAnswerAsync(appId, conversationId, question, citations);
            return answer;
        }
    }
}
