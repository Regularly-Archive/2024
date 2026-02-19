using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Core;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
    [KernelPlugin(Description = "基于检索增强生成（RAG）的问答插件。从知识库中检索相关文档并生成答案，支持引用标注", Enabled = true, Version = "1.0")]
    public class RAGFlowPlugin : BasePlugin
    {
        public RAGFlowPlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {

        }

        [KernelFunction]
        [Description("根据用户问题检索知识库并生成答案。返回的答案中会包含引用标记（如 [1]），指向参考的文档片段。")]
        private async Task<string> RetrieveAndGenerateAnswerAsync(
            [Description("应用 ID，用于定位要查询的知识库")] long appId,
            [Description("会话 ID，用于记录对话历史")] string conversationId,
            [Description("用户的问题")] string question,
            [Description("是否允许联网搜索获取额外信息，默认为 false")] bool enableWebSearch,
            Kernel kernel
        )
        {
            var memoryService = _serviceProvider.GetService<IMemoryService>();
            var chatHistoryService = _serviceProvider.GetService<IChatHistoriesService>();
            var citationService = _serviceProvider.GetService<CitationService>();

            var ragFlowService = new RAGFlowService(kernel, _serviceProvider, memoryService, chatHistoryService, citationService);
            var citations = await ragFlowService.GenerateCitationsAsync(appId, question, enableWebSearch);
            var ragResult = await ragFlowService.GenerateAnswerAsync(appId, conversationId, question, citations);

            kernel.GetAgentExecutionContext().AddCitations(ragResult.AnswerSources);

            return ragResult.PlainAnswer;
        }
    }
}
