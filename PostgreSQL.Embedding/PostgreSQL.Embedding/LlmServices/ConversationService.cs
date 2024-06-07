using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Text;

namespace PostgreSQL.Embedding.LlmServices
{
    public class ConversationService : IConversationService
    {
        private readonly IMemoryService _memoryService;
        private readonly IKernelService _kernelService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IRepository<LlmApp> _llmAppRepository;
        private readonly IChatHistoryService _chatHistoryService;
        public ConversationService(
            IServiceProvider serviceProvider,
            IRepository<LlmApp> llmAppRepository,
            IKernelService kernelService,
            IMemoryService memoryService,
            IChatHistoryService chatHistoryService
            )
        {
            _kernelService = kernelService;
            _memoryService = memoryService;
            _serviceProvider = serviceProvider;
            _llmAppRepository = llmAppRepository;
            _chatHistoryService = chatHistoryService;
        }

        public async Task Invoke(OpenAIModel model, long appId, HttpContext HttpContext)
        {
            var app = await _llmAppRepository.GetAsync(appId);
            var kernel = await _kernelService.GetKernel(app);

            var input = model.messages[model.messages.Count - 1].content;
            switch (app.AppType)
            {
                case (int)LlmAppType.Chat:
                    var genericChatService = new GenericConversationService(kernel, app, _serviceProvider, _chatHistoryService);
                    await genericChatService.InvokeAsync(model, HttpContext, input);
                    break;
                case (int)LlmAppType.Knowledge:
                    var ragChatService = new RAGConversationService(kernel, app, _serviceProvider, _memoryService, _chatHistoryService);
                    await ragChatService.InvokeAsync(model, HttpContext, input);
                    break;
            }
        }

        private async Task<string> SummarizeHistories(OpenAIModel model, Kernel kernel)
        {
            StringBuilder history = new StringBuilder();
            string questions = model.messages[model.messages.Count - 1].content;
            for (int i = 0; i < model.messages.Count() - 1; i++)
            {
                var item = model.messages[i];
                history.Append($"{item.role}:{item.content}{Environment.NewLine}");
            }

            if (model.messages.Count() > 10)
            {
                //历史会话大于10条，进行总结
                var msg = await SummarizeContent(kernel, history.ToString());
                return msg;
            }
            else
            {
                var msg = $"history：{history.ToString()}{Environment.NewLine} user：{questions}"; ;
                return msg;
            }
        }

        public async Task<string> SummarizeContent(Kernel kernel, string content)
        {
            var summaryFunction = kernel.Plugins.GetFunction("ConversationSummaryPlugin", "SummarizeConversation");
            if (summaryFunction == null)
                return string.Empty;

            var input = $"请使用中文对下面的内容进行归纳和总结: {content}";
            var summary = await kernel.InvokeAsync(summaryFunction, new() { ["input"] = input });

            return summary.GetValue<string>();
        }
    }
}
