using Microsoft.Extensions.Options;
using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Plugins.Core;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Reflection.Metadata;
using System.Text;

namespace PostgreSQL.Embedding.LlmServices
{
    public class ChatService : IChatService
    {
        private readonly IServiceProvider _serviceProvider;
        public ChatService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task Chat(OpenAIModel model, string sk, HttpContext HttpContext)
        {
            // Todo: 从 sk 中解析应用信息

            var app = new LlmApp();
            var kernel = CreateKernel(app);

            var input = await SummarizeHistories(model, kernel);
            switch (app.AppType)
            {
                case (int)LlmAppType.Chat:
                    var genericChatService = new GenericChatService(kernel, app);
                    await genericChatService.HandleChat(model, HttpContext, input);
                    break;
                case (int)LlmAppType.Knowledge:
                    var knowledgeBasedChatService = new KnowledgeBaseChatService(kernel, app, _serviceProvider);
                    await knowledgeBasedChatService.HandleKnowledge(HttpContext, input);
                    break;
            }
        }

        private Kernel CreateKernel(LlmApp app)
        {
            var options = _serviceProvider.GetRequiredService<IOptions<LlmConfig>>();

            // 提取 App 的服务提供商
            app.ServiceProvider = (int)LlmServiceProvider.LLama;

            var httpClient = new HttpClient(new OpenAIChatHandler(options, (LlmServiceProvider)app.ServiceProvider));
            var kernel = Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(modelId: app.TextModel, apiKey: "sk-1234567890", httpClient: httpClient)
                .Build();

            return kernel;
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
