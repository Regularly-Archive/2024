using Microsoft.Extensions.Options;
using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using SqlSugar;
using System.Reflection.Metadata;
using System.Text;

namespace PostgreSQL.Embedding.LlmServices
{
    public class conversationService : IConversationService
    {
        private readonly IMemoryService _memoryService;
        private readonly IKernelService _kernelService;
        private readonly IServiceProvider _serviceProvider;
        private readonly SimpleClient<LlmApp> _llmAppRepository;
        public conversationService(IServiceProvider serviceProvider, SimpleClient<LlmApp> llmAppRepository, IKernelService kernelService, IMemoryService memoryService)
        {
            _kernelService = kernelService;
            _memoryService = memoryService;
            _serviceProvider = serviceProvider;
            _llmAppRepository = llmAppRepository;
        }

        public async Task Chat(OpenAIModel model, string sk, HttpContext HttpContext)
        {
            // Todo: 从 sk 中解析应用信息
            var appId = long.Parse(sk);

            var app = _llmAppRepository.GetById(appId);
            var kernel = await _kernelService.GetKernel(app);

            var input = await SummarizeHistories(model, kernel);
            switch (app.AppType)
            {
                case (int)LlmAppType.Chat:
                    var genericChatService = new GenericChatService(kernel, app);
                    await genericChatService.HandleChat(model, HttpContext, input);
                    break;
                case (int)LlmAppType.Knowledge:
                    var knowledgeBasedChatService = new RAGChatService(kernel, app, _serviceProvider, _memoryService);
                    await knowledgeBasedChatService.HandleKnowledge(HttpContext, input);
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
                var msg = await HistorySummarize(kernel, questions, history.ToString());
                return msg;
            }
            else
            {
                var msg = $"history：{history.ToString()}{Environment.NewLine} user：{questions}"; ;
                return msg;
            }
        }

        public async Task<string> HistorySummarize(Kernel kernel, string questions, string history)
        {
            KernelFunction sunFun = kernel.Plugins.GetFunction("ConversationSummaryPlugin", "SummarizeConversation");
            var summary = await kernel.InvokeAsync(sunFun, new() { ["input"] = $"内容是：{history.ToString()} {Environment.NewLine} 请注意用中文总结" });
            var msg = $"history：{history.ToString()}{Environment.NewLine} user：{questions}"; ;
            return msg;
        }
    }
}
