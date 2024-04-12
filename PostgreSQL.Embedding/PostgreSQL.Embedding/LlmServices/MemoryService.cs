using Microsoft.Extensions.Options;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Configuration;
using Microsoft.KernelMemory.Postgres;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.Utils;
using SqlSugar;

namespace PostgreSQL.Embedding.LlmServices
{
    public class MemoryService : IMemoryService
    {
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;

        private readonly IRepository<LlmModel> _llmModelRepository;
        private readonly IRepository<LlmAppKnowledge> _llmAppKnowledgeRepository;
        private readonly IRepository<KnowledgeBase> _knowledgeBaseRepository;
        private readonly IRepository<TablePrefixMapping> _tablePrefixMappingRepository;

        public MemoryService(IConfiguration configuration, IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _llmModelRepository = serviceProvider.GetService<IRepository<LlmModel>>();
            _llmAppKnowledgeRepository = serviceProvider.GetService<IRepository<LlmAppKnowledge>>();
            _knowledgeBaseRepository = serviceProvider.GetService<IRepository<KnowledgeBase>>();
            _tablePrefixMappingRepository = serviceProvider.GetService<IRepository<TablePrefixMapping>>();
        }

        public async Task<MemoryServerless> CreateByApp(LlmApp app)
        {
            var generationModel = await _llmModelRepository.SingleOrDefaultAsync(x => x.ModelType == (int)ModelType.TextGeneration && x.ModelName == app.TextModel);

            var embeddingModelId = await GetEmbeddingModelByKnowledges(app);
            var embeddingModel = await _llmModelRepository.SingleOrDefaultAsync(x => x.ModelType == (int)ModelType.TextEmbedding && x.ModelName == embeddingModelId);

            var options = _serviceProvider.GetRequiredService<IOptions<LlmConfig>>();
            var embeddingHttpClient = new HttpClient(new EmbeddingRouterHandler(embeddingModel, options));
            var generationHttpClient = new HttpClient(new OpenAIChatHandler(generationModel, options));

            var tableNamePrefix = await GenerateTableNamePrefix(embeddingModel);

            var postgresConfig = new PostgresConfig()
            {
                ConnectionString = _configuration["ConnectionStrings:Default"]!,
                TableNamePrefix = tableNamePrefix,
            };

            // Todo
            // 需要解除对 OpenAIConfig 的依赖
            var openAIConfig = new OpenAIConfig();
            _configuration.BindSection(nameof(OpenAIConfig), openAIConfig);

            var memoryBuilder = new KernelMemoryBuilder();

            memoryBuilder
                .WithPostgresMemoryDb(postgresConfig)
                .WithOpenAITextGeneration(openAIConfig, httpClient: generationHttpClient)
                .WithOpenAITextEmbeddingGeneration(openAIConfig, httpClient: embeddingHttpClient)
                .WithCustomTextPartitioningOptions(new TextPartitioningOptions()
                {
                    MaxTokensPerParagraph = DefaultTextPartitioningOptions.MaxTokensPerParagraph,
                    MaxTokensPerLine = DefaultTextPartitioningOptions.MaxTokensPerLine,
                    OverlappingTokens = DefaultTextPartitioningOptions.OverlappingTokens
                })
                .WithSearchClientConfig(new SearchClientConfig()
                {
                    EmptyAnswer = "抱歉，我无法回答你的问题！"
                });

            return memoryBuilder.Build<MemoryServerless>();
        }

        public async Task<MemoryServerless> CreateByKnowledgeBase(KnowledgeBase knowledgeBase)
        {
            var embeddingModel = await _llmModelRepository.SingleOrDefaultAsync(x => x.ModelType == (int)ModelType.TextEmbedding && x.ModelName == knowledgeBase.EmbeddingModel);
            var generationModel = await _llmModelRepository.SingleOrDefaultAsync(x => x.ModelType == (int)ModelType.TextGeneration && x.ModelName == "gpt-3.5-turbo");

            var options = _serviceProvider.GetRequiredService<IOptions<LlmConfig>>();
            var embeddingHttpClient = new HttpClient(new EmbeddingRouterHandler(embeddingModel, options));
            var generationHttpClient = new HttpClient();

            var tableNamePrefix = await GenerateTableNamePrefix(embeddingModel);

            var postgresConfig = new PostgresConfig()
            {
                ConnectionString = _configuration["ConnectionStrings:Default"]!,
                TableNamePrefix = tableNamePrefix,
            };

            // Todo
            // 需要解除对 OpenAIConfig 的依赖
            var openAIConfig = new OpenAIConfig();
            _configuration.BindSection(nameof(OpenAIConfig), openAIConfig);
            openAIConfig.EmbeddingModel = embeddingModel.ModelName;
            openAIConfig.TextModel = generationModel.ModelName;

            var memoryBuilder = new KernelMemoryBuilder();

            memoryBuilder
                .WithPostgresMemoryDb(postgresConfig)
                .WithOpenAITextGeneration(openAIConfig, httpClient: generationHttpClient)
                .WithOpenAITextEmbeddingGeneration(openAIConfig, httpClient: embeddingHttpClient)
                .WithCustomTextPartitioningOptions(new TextPartitioningOptions()
                {
                    MaxTokensPerParagraph = (knowledgeBase.MaxTokensPerParagraph.HasValue ?
                        knowledgeBase.MaxTokensPerParagraph.Value :
                        DefaultTextPartitioningOptions.MaxTokensPerParagraph),

                    MaxTokensPerLine = (knowledgeBase.MaxTokensPerLine.HasValue ?
                        knowledgeBase.MaxTokensPerLine.Value :
                        DefaultTextPartitioningOptions.MaxTokensPerLine),

                    OverlappingTokens = (knowledgeBase.OverlappingTokens.HasValue ?
                    knowledgeBase.OverlappingTokens.Value :
                    DefaultTextPartitioningOptions.OverlappingTokens),
                })
                .WithSearchClientConfig(new SearchClientConfig()
                {
                    EmptyAnswer = "抱歉，我无法回答你的问题！"
                });

            return memoryBuilder.Build<MemoryServerless>();
        }

        private async Task<string> GenerateTableNamePrefix(LlmModel embeddingModel)
        {
            var tablePrefixMapping = await _tablePrefixMappingRepository.SingleOrDefaultAsync(x => x.FullName == embeddingModel.ModelName);
            if (tablePrefixMapping != null)
                return $"sk-{tablePrefixMapping.ShortName.ToLower()}-";

            var shortCode = string.Empty;
            while (string.IsNullOrEmpty(shortCode))
            {
                shortCode = ShortUrlGenerator.GenerateShortCode(embeddingModel.ModelName);
            }

            await _tablePrefixMappingRepository.AddAsync(new TablePrefixMapping()
            {
                FullName = embeddingModel.ModelName,
                ShortName = shortCode,
            });

            return $"sk-{shortCode.ToLower()}-";
        }

        private async Task<string> GetEmbeddingModelByKnowledges(LlmApp app)
        {
            var llmAppKnowledges = await _llmAppKnowledgeRepository.FindAsync(x => x.AppId == app.Id);
            if (llmAppKnowledges.Any())
            {
                var knowledgeBaseId = llmAppKnowledges.FirstOrDefault().KnowledgeBaseId;
                var knowledageBase = await _knowledgeBaseRepository.GetAsync(knowledgeBaseId);
                if (knowledageBase != null)
                    return knowledageBase.EmbeddingModel;
            }

            return "GanymedeNil/text2vec-large-chinese";
        }
    }
}
