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
            var kernal = CreateKernel(app);

            switch (app.AppType)
            {
                case (int)LlmAppType.Chat:
                    await HandleChat(model, HttpContext, kernal);
                    break;
                case (int)LlmAppType.Knowledge:
                    await HandleKnowledge()
                    break;
            }
        }

        private async Task HandleChat(OpenAIModel model, HttpContext HttpContext, Kernel kernel)
        {
            if (model.stream)
            {
                var result = new OpenAIStreamResult();
                result.created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                result.choices = new List<StreamChoicesModel>() { new StreamChoicesModel() { delta = new OpenAIMessage() { role = "assistant" } } };
                await HandleStramingChat(HttpContext, result, app, msg);
                HttpContext.Response.ContentType = "application/json";
                await HttpContext.Response.WriteAsync(JsonConvert.SerializeObject(result1));
                await HttpContext.Response.CompleteAsync();
                return;
            }
            else
            {
                OpenAIResult result2 = new OpenAIResult();
                result2.created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                result2.choices = new List<ChoicesModel>() { new ChoicesModel() { message = new OpenAIMessage() { role = "assistant" } } };
                result2.choices[0].message.content = await SendChat(msg, app);
                HttpContext.Response.ContentType = "application/json";
                await HttpContext.Response.WriteAsync(JsonConvert.SerializeObject(result2));
                await HttpContext.Response.CompleteAsync();
            }
        }

        private async Task HandleStramingChat(HttpContext HttpContext, OpenAIStreamResult result, Apps app, string msg)
        {
            var _kernel = _kernelService.GetKernel();
            var temperature = app.Temperature / 100;//存的是0~100需要缩小
            OpenAIPromptExecutionSettings settings = new() { Temperature = temperature };
            HttpContext.Response.Headers.Add("Content-Type", "text/event-stream");

            if (string.IsNullOrEmpty(app.Prompt) || !app.Prompt.Contains("{{$input}}"))
            {
                //如果模板为空，给默认提示词
                app.Prompt = app.Prompt.ConvertToString() + "{{$input}}";
            }
            //var promptTemplateFactory = new KernelPromptTemplateFactory();
            //var promptTemplate = promptTemplateFactory.Create(new PromptTemplateConfig(app.Prompt));
            //var renderedPrompt = await promptTemplate.RenderAsync(_kernel);
            //Console.WriteLine(renderedPrompt);

            var func = _kernel.CreateFunctionFromPrompt(app.Prompt, settings);
            var chatResult = _kernel.InvokeStreamingAsync<StreamingChatMessageContent>(function: func, arguments: new KernelArguments() { ["input"] = msg });
            int i = 0;

            await foreach (var content in chatResult)
            {
                result.choices[0].delta.content = content.Content.ConvertToString();
                string message = $"data: {JsonConvert.SerializeObject(result)}\n\n";
                await HttpContext.Response.WriteAsync(message, Encoding.UTF8);
                await HttpContext.Response.Body.FlushAsync();
                //模拟延迟。
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }

            await HttpContext.Response.WriteAsync("data: [DONE]");
            await HttpContext.Response.Body.FlushAsync();

            await HttpContext.Response.CompleteAsync();
        }

        private async Task<string> SendChat(string msg, LlmApp app)
        {
            string result = "";
            if (string.IsNullOrEmpty(app.Prompt) || !app.Prompt.Contains("{{$input}}"))
            {
                //如果模板为空，给默认提示词
                app.Prompt = app.Prompt.ConvertToString() + "{{$input}}";
            }
            var _kernel = _kernelService.GetKernel();
            var temperature = app.Temperature / 100;//存的是0~100需要缩小
            OpenAIPromptExecutionSettings settings = new() { Temperature = temperature };
            if (!string.IsNullOrEmpty(app.ApiFunctionList))
            {
                _kernelService.ImportFunctionsByApp(app, _kernel);
                settings = new() { ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions, Temperature = temperature };
            }
            var promptTemplateFactory = new KernelPromptTemplateFactory();
            var promptTemplate = promptTemplateFactory.Create(new PromptTemplateConfig(app.Prompt));

            var func = _kernel.CreateFunctionFromPrompt(app.Prompt, settings);
            var chatResult = await _kernel.InvokeAsync(function: func, arguments: new KernelArguments() { ["input"] = msg });
            if (chatResult.IsNotNull())
            {
                string answers = chatResult.GetValue<string>();
                result = answers;
            }
            return result;
        }

        private async Task HandleKnowledge(OpenAIModel model, HttpContext HttpContext, Kernel kernel, string message)
        {
            OpenAIResult result3 = new OpenAIResult();
            result3.created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            result3.choices = new List<ChoicesModel>() { new ChoicesModel() { message = new OpenAIMessage() { role = "assistant" } } };
            result3.choices[0].message.content = await SendKms(msg, app);
            HttpContext.Response.ContentType = "application/json";
            await HttpContext.Response.WriteAsync(JsonConvert.SerializeObject(result3));
            await HttpContext.Response.CompleteAsync();
        }

        private async Task<string> SendKms(string message, LlmApp app, Kernel kernel)
        {
            var _memory = _kMService.GetMemory();
            string result = "";
            //知识库问答
            var filters = new List<MemoryFilter>();

            var kmsidList = app.KmsIdList.Split(",");
            foreach (var kmsid in kmsidList)
            {
                filters.Add(new MemoryFilter().ByTag("kmsid", kmsid));
            }

            var xlresult = await _memory.SearchAsync(message, index: "kms", filters: filters);
            string dataMsg = "";
            if (xlresult != null)
            {
                foreach (var item in xlresult.Results)
                {
                    foreach (var part in item.Partitions)
                    {
                        dataMsg += $"[file:{item.SourceName};Relevance:{(part.Relevance * 100).ToString("F2")}%]:{part.Text}{Environment.NewLine}";
                    }
                }
                KernelFunction jsonFun = _kernel.Plugins.GetFunction("KMSPlugin", "Ask");
                var chatResult = await _kernel.InvokeAsync(function: jsonFun,
                    arguments: new KernelArguments() { ["doc"] = dataMsg, ["history"] = "", ["questions"] = message });
                if (chatResult.IsNotNull())
                {
                    string answers = chatResult.GetValue<string>();
                    result = answers;
                }
            }
            return result;
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
                var msg = await _kernelService.HistorySummarize(_kernel, questions, history.ToString());
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
