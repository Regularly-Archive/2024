using JetBrains.Annotations;
using LLama;
using LLamaSharp.KernelMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Postgres;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.Handlers;
using PostgreSQL.Embedding.LlmServices.Abstration;

namespace PostgreSQL.Embedding.LlmServices
{
    public class KnowledgeBaseService : IKnowledgeBaseService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;

        private const string Tag_KnowledgeBaseId = "_knowledgeBaseId";
        private const string Tag_TaskId = "_taskId";

        public KnowledgeBaseService(IServiceProvider serviceProvider, IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
        }

        public Task<List<KnowledgeDetail>> GetKnowledgeBaseDetails(long knowledgeBaseId)
        {
            // 查询知识库
            var knowledge = new KnowledgeBase();
            var memoryServerless = CreateMemoryServerless(knowledge);

            // Todo:
            var memoryFilter = new MemoryFilter();
            memoryFilter.ByTag(Tag_KnowledgeBaseId, knowledgeBaseId.ToString());


            return Task.FromResult(new List<KnowledgeDetail>());
        }

        public async Task ImportKnowledgeFromFiles(string taskId, long knowledgeBaseId, IEnumerable<string> files)
        {
            // 查询知识库
            var knowledge = new KnowledgeBase();
            var memoryServerless = CreateMemoryServerless(knowledge);

            var tags = new TagCollection
            {
                { Tag_TaskId, taskId },
                { Tag_KnowledgeBaseId, knowledgeBaseId.ToString() },
            };
            var document = new Document(tags: tags, filePaths: files);
            await memoryServerless.ImportDocumentAsync(document);
        }

        public async Task ImportKnowledgeFromUrl(string taskId, long knowledgeBaseId, string url)
        {
            // 查询知识库
            var knowledge = new KnowledgeBase();
            var memoryServerless = CreateMemoryServerless(knowledge);

            var tags = new TagCollection();
            tags.Add(Tag_TaskId, taskId);
            tags.Add(Tag_KnowledgeBaseId, knowledgeBaseId.ToString());
            await memoryServerless.ImportWebPageAsync(url, tags:tags);
        }

        public Task DeleteKnowledgeById(long knowledgeBaseId)
        {
            // 查询知识库
            var knowledge = new KnowledgeBase();
            var memoryServerless = CreateMemoryServerless(knowledge);

            // Todo
            return Task.CompletedTask;
        }

        public Task DeleteKnowledgeByFileName(long knowledgeBaseId, string fileName)
        {
            // 查询知识库
            var knowledge = new KnowledgeBase();
            var memoryServerless = CreateMemoryServerless(knowledge);

            // Todo
            return Task.CompletedTask;
        }

        private MemoryServerless CreateMemoryServerless(KnowledgeBase knowledgeBase)
        {
            var options = _serviceProvider.GetRequiredService<IOptions<LlmConfig>>();
            var httpClient = new HttpClient(new OpenAIEmbeddingHandler(options, LlmServiceProvider.LLama));

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
