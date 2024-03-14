﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Configuration;
using Microsoft.KernelMemory.Postgres;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Plugins.Memory;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using SqlSugar;

namespace PostgreSQL.Embedding.LlmServices
{
    public class MemoryService : IMemoryService
    {
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        
        private readonly SimpleClient<LlmModel> _llmModelRepository;
        private readonly SimpleClient<LlmAppKnowledge> _llmAppKnowledgeRepository;
        private readonly SimpleClient<KnowledgeBase> _knowledgeBaseRepository;

        public MemoryService(IConfiguration configuration, IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _llmModelRepository = serviceProvider.GetService<SimpleClient<LlmModel>>();
            _llmAppKnowledgeRepository = serviceProvider.GetService<SimpleClient<LlmAppKnowledge>>();
            _knowledgeBaseRepository = serviceProvider.GetService<SimpleClient<KnowledgeBase>>();
        }

        public async Task<MemoryServerless> CreateByApp(LlmApp app)
        {
            var generationModel = await _llmModelRepository.GetFirstAsync(x => x.ModelType == (int)ModelType.TextGeneration && x.ModelName == app.TextModel);

            var embeddingModelId = await GetEmbeddingModelByKnowledges(app);
            var embeddingModel = await _llmModelRepository.GetFirstAsync(x => x.ModelType == (int)ModelType.TextEmbedding && x.ModelName == embeddingModelId); 

            var options = _serviceProvider.GetRequiredService<IOptions<LlmConfig>>();
            var embeddingHttpClient = new HttpClient(new OpenAIEmbeddingHandler(embeddingModel, options));
            var generationHttpClient = new HttpClient();

            var postgresConfig = new PostgresConfig()
            {
                ConnectionString = _configuration["ConnectionStrings:Default"]!,
                TableNamePrefix = $"sk_",
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
                    MaxTokensPerLine =DefaultTextPartitioningOptions.MaxTokensPerLine,
                    OverlappingTokens = DefaultTextPartitioningOptions.OverlappingTokens
                });

            return memoryBuilder.Build<MemoryServerless>();
        }

        public async Task<MemoryServerless> CreateByKnowledgeBase(KnowledgeBase knowledgeBase)
        {
            var embeddingModel = await _llmModelRepository.GetFirstAsync(x => x.ModelType == (int)ModelType.TextEmbedding && x.ModelName == knowledgeBase.EmbeddingModel);
            var generationModel = await _llmModelRepository.GetFirstAsync(x => x.ModelType == (int)ModelType.TextGeneration && x.ModelName == "gpt-3.5-turbo");

            var options = _serviceProvider.GetRequiredService<IOptions<LlmConfig>>();
            var embeddingHttpClient = new HttpClient(new OpenAIEmbeddingHandler(embeddingModel, options));
            var generationHttpClient = new HttpClient();

            var postgresConfig = new PostgresConfig()
            {
                ConnectionString = _configuration["ConnectionStrings:Default"]!,
                TableNamePrefix = $"sk_"
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
                    MaxTokensPerParagraph = (knowledgeBase.MaxTokensPerParagraph.HasValue ?
                        knowledgeBase.MaxTokensPerParagraph.Value :
                        DefaultTextPartitioningOptions.MaxTokensPerParagraph),

                    MaxTokensPerLine = (knowledgeBase.MaxTokensPerLine.HasValue ?
                        knowledgeBase.MaxTokensPerLine.Value :
                        DefaultTextPartitioningOptions.MaxTokensPerLine),

                    OverlappingTokens = (knowledgeBase.OverlappingTokens.HasValue ?
                    knowledgeBase.OverlappingTokens.Value :
                    DefaultTextPartitioningOptions.OverlappingTokens),
                });

            return memoryBuilder.Build<MemoryServerless>();
        }

        private async Task<string> GetEmbeddingModelByKnowledges(LlmApp app)
        {
            var llmAppKnowledges = await _llmAppKnowledgeRepository.GetListAsync(x => x.AppId == app.Id);
            if (llmAppKnowledges.Any())
            {
                var knowledgeBaseId = llmAppKnowledges.FirstOrDefault().KnowledgeBaseId;
                var knowledageBase = await _knowledgeBaseRepository.GetByIdAsync(knowledgeBaseId);
                if (knowledageBase != null)
                    return knowledageBase.EmbeddingModel;
            }

            return "GanymedeNil/text2vec-large-chinese";
        }
    }
}
