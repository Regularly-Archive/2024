using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Postgres;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Plugins.Memory;
using System.Net.Http;
using System.Text;

namespace SK.RAG
{
    class OpenAIProxyHandler : HttpClientHandler
    {
        private string _proxyUrl;
        public OpenAIProxyHandler(string proxyUrl)
        {
            _proxyUrl = proxyUrl;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri = new Uri(_proxyUrl);
            return base.SendAsync(request, cancellationToken);
        }
    }
    class Program
    {

        static MemoryServerless GetMemoryServerless()
        {
            var openaiConfig = new OpenAIConfig()
            {
                APIKey = "",
                EmbeddingModel = "BAAI/bge-base-zh-v1.5",
                TextModel = "gpt-3.5-turbo"
            };

            // Semantic Memory 中的 ITextEmbeddingGenerator 每次只能对一个文本做向量嵌入
            // 此时，调用 OpenAI 的 Embedding 接口会被官方限流，为此，我们使用一个本地的服务来进行替代
            var embeddingHttpClient = new HttpClient(new OpenAIProxyHandler("http://localhost:8003/v1/embeddings"));

            return new KernelMemoryBuilder()
                .WithPostgresMemoryDb(new PostgresConfig()
                {
                    ConnectionString = "User ID=postgresql;Password=postgresql;Host=localhost;Port=5432;Database=wiki;Pooling=true;MaxPoolSize=100",
                    TableNamePrefix = "sk-training-"
                })
                .WithOpenAITextEmbeddingGeneration(openaiConfig, httpClient: embeddingHttpClient)
                .WithOpenAITextGeneration(openaiConfig)
                .WithCustomTextPartitioningOptions(new Microsoft.KernelMemory.Configuration.TextPartitioningOptions()
                {
                    MaxTokensPerParagraph = 1000,
                    MaxTokensPerLine = 500,
                    OverlappingTokens = 250
                })
                .WithSearchClientConfig(new SearchClientConfig()
                {
                    EmptyAnswer = "抱歉，我无法回答你的问题！"
                })
                .Build<MemoryServerless>();

        }

        static async Task ImportDocument(MemoryServerless memoryServerless)
        {
            // 导入文件
            var fileNames = new List<string> { "白马啸西风", "越女剑", "鸳鸯刀" };
            foreach (var fileName in fileNames)
            {
                var document = new Microsoft.KernelMemory.Document();
                document
                    .AddFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", $"{fileName}.txt"))
                    .AddTag("FileName", fileName);

                await memoryServerless.ImportDocumentAsync(document);
            }

            // 导入文本
            await memoryServerless.ImportTextAsync("人生若只如初见，何事秋风悲画扇。等闲变却故人心，却道故人心易变。");

            // 导入网页
            await memoryServerless.ImportWebPageAsync("https://blog.yuanpei.me/about");

        }

        static async Task<string> BuildKnowledgeContext(MemoryServerless memoryServerless, string input, string fileName = null)
        {
            var filters = new List<MemoryFilter>();
            if (!string.IsNullOrEmpty(fileName))
            {
                filters.Add(new MemoryFilter().ByTag("FileName", fileName));
            }

            // 构建上下文
            var contextBuilder = new StringBuilder();
            var searchResult = await memoryServerless.SearchAsync(input, filters: filters, minRelevance: 0, limit: 5);

            if (searchResult.Results.Any())
            {
                foreach (var citation in searchResult.Results)
                {
                    foreach (var part in citation.Partitions)
                    {
                        contextBuilder.AppendLine($"fileName:{citation.SourceName}; Relevance:{(part.Relevance * 100).ToString("F2")}%; Content: {part.Text}\r\n");
                    }
                }

                return contextBuilder.ToString();
            }

            return string.Empty;
        }

        static async Task Main(string[] args)
        {
            var memoryServerless = GetMemoryServerless();

            var openaiProxyHandler = new OpenAIProxyHandler("https://api.moonshot.cn/v1/chat/completions");
            var httpClient = new HttpClient(openaiProxyHandler);

            var kernel = Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(
                    modelId: "moonshot-v1-8k",
                    apiKey: "",
                    httpClient: httpClient
                )
                .Build();

            // 导入文档
            // await ImportDocument(memoryServerless);

            // 定义提示词模板
            var promptTemplate =@"""
                                
                Role:
                You are a helpful AI bot. Your name is {{$name}}.

                Act:
                Please answer the question only based on the following context:

                {{$context}}

                Rules:
                1. If the question is about your identity or role or name, answer '{{$name}}' directly, without the need to refer to the context
                2. If the context is not enough to support the generation of an answer, Please return ""I'm sorry, I can't anser your question."" immediately.
                3. You have an opportunity to refine the existing answer (only if needed) with current context.
                4. You must always answer the question in Chinese. 

                Your Question is: 

                {{$question}}
                
            """;
            var chatFunction = kernel.CreateFunctionFromPrompt(promptTemplate);

            var input = string.Empty;
            while ((input = Console.ReadLine()) != null)
            {
                Console.WriteLine($"User -> {input}");

                // 从向量数据库中检索内容，并将其作为上下文发送给大模型
                var context = await BuildKnowledgeContext(memoryServerless, input);
                var arguments = new KernelArguments() { ["context"] = context, ["name"] = "ChatGPT", ["question"] = input };

                Console.Write("AI -> ");
                await foreach (var message in kernel.InvokeStreamingAsync<string>(chatFunction, arguments: arguments))
                {
                    Console.Write(message);
                }
                Console.Write("\r\n");

                Console.ForegroundColor = ConsoleColor.Blue;
                Console.Write($"引用文档：\r\n {context}\r\n");
                Console.ResetColor();
            }
        }
    }
}
