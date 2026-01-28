using LLama;
using LLama.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Core;
using PostgreSQL.Embedding.Llm.Services;

namespace PostgreSQL.Embedding.Llm.Core
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加 LLM 核心服务
        /// </summary>
        public static IServiceCollection AddLlmCore(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // 注册核心服务
            services.AddScoped<IKernelService, KernalService>();
            services.AddScoped<IMemoryService, MemoryService>();
            services.AddScoped<IConversationService, ConversationService>();
            services.AddScoped<IChatHistoriesService, ChatHistoriesService>();
            services.AddScoped<PromptTemplateService>();

            // 注册知识库服务
            services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();
            services.AddScoped<IKnowledgeBaseTaskQueueService, KnowledgeBaseTaskQueueService>();

            // 注册 LLM 服务工厂
            services.AddSingleton<ILlmServiceFactory, LlmServiceFactory>();

            // 注册后台服务
            services.AddSingleton<KnowledgeBaseBackgroundService>();
            services.AddHostedService<KnowledgeBaseBackgroundService>();

            return services;
        }

        /// <summary>
        /// 添加 LLama 嵌入服务
        /// </summary>
        public static IServiceCollection AddLLamaEmbedder(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var modelPath = configuration.GetValue<string>("LLamaConfig:ModelPath")
                    ?? throw new InvalidOperationException("LLamaConfig:ModelPath is not configured."); 

            var contextSize = configuration.GetValue<uint>("LLamaConfig:ContextSize");

            var @params = new ModelParams(modelPath)
            {
                ContextSize = contextSize
            };

            using var weights = LLamaWeights.LoadFromFile(@params);
            var embedder = new LLamaEmbedder(weights, @params);

            services.AddSingleton(embedder);

            return services;
        }
    }
}
