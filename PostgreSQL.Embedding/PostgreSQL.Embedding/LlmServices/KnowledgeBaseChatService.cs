using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Postgres;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.LlmServices
{
    public class KnowledgeBaseChatService
    {
        private readonly Kernel _kernel;
        private readonly LlmApp _app;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        public KnowledgeBaseChatService(Kernel kernel, LlmApp app, IServiceProvider serviceProvider)
        {
            _kernel = kernel;
            _app = app;
            _serviceProvider = serviceProvider;
            _configuration = serviceProvider.GetRequiredService<IConfiguration>();
        }

        public async Task HandleKnowledge(HttpContext HttpContext, string input)
        {
            var result = new OpenAIResult();
            result.created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            result.choices = new List<ChoicesModel>() { new ChoicesModel() { message = new OpenAIMessage() { role = "assistant" } } };
            result.choices[0].message.content = await QueryWithMemories(input);
            HttpContext.Response.ContentType = "application/json";
            await HttpContext.Response.WriteAsync(JsonConvert.SerializeObject(result));
            await HttpContext.Response.CompleteAsync();
        }

        private async Task<string> QueryWithMemories(string input)
        {
            var _memory = CreateMemorylessByApp(_app);
            string result = "";

            // Todo: 从 App 找出关联的知识库
            var filters = new List<MemoryFilter>();

            var knowledgeBaseIds = new List<long>();
            foreach (var kbId in knowledgeBaseIds)
            {
                filters.Add(new MemoryFilter().ByTag("_knowledgeBaseId", kbId.ToString()));
            }

            var xlresult = await _memory.SearchAsync(input, index: "kms", filters: filters);
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
                    arguments: new KernelArguments() { ["doc"] = dataMsg, ["history"] = "", ["questions"] = input });

                var answers = chatResult.GetValue<string>();
                if (!string.IsNullOrEmpty(answers)) return answers;
            }
            return result;
        }

        // Todo
        private MemoryServerless CreateMemorylessByApp(LlmApp app)
        {
            var options = _serviceProvider.GetRequiredService<IOptions<LlmConfig>>();
            var httpClient = new HttpClient(new OpenAIEmbeddingHandler(options, (LlmServiceProvider)app.ServiceProvider));

            var postgresConfig = new PostgresConfig()
            {
                ConnectionString = _configuration["ConnectionStrings:Default"]!,
                TableNamePrefix = "sk_"
            };

            var openAIConfig = new OpenAIConfig();
            _configuration.BindSection(nameof(OpenAIConfig), openAIConfig);

            var memoryBuilder = new KernelMemoryBuilder();
            memoryBuilder
                .WithPostgresMemoryDb(postgresConfig)
                .WithOpenAITextGeneration(openAIConfig, httpClient: httpClient)
                .WithOpenAITextEmbeddingGeneration(openAIConfig, httpClient: httpClient);

            return memoryBuilder.Build<MemoryServerless>();
        }
    }
}
