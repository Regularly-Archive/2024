using LLama;
using LLama.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Core;
using PostgreSQL.Embedding.Llm.Core.ChatHistory.Models;
using PostgreSQL.Embedding.Llm.Core.ChatHistory.Services;
using PostgreSQL.Embedding.Llm.Services;
using PostgreSQL.Embedding.Plugins;

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
            services.AddScoped<ISkillService, SkillService>();
            services.AddScoped<IConversationService, ConversationService>();
            services.AddScoped<IChatHistoriesService, ChatHistoriesService>();
            services.AddScoped<PromptTemplateService>();

            services.AddScoped(sp =>
            {
                var config = new ChatHistoryConfig
                {
                    ActiveRounds = 5,
                    BufferRounds = 3
                };

                var kernel = sp.GetRequiredService<Microsoft.SemanticKernel.Kernel>();
                var chatHistoriesService = sp.GetRequiredService<IChatHistoriesService>();
                var stateRepository = sp.GetRequiredService<IRepository<AppConversationState>>();
                return new ChatHistoryManager(config, kernel, chatHistoriesService, stateRepository);
            });

            // 注册知识库服务
            services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();
            services.AddScoped<IKnowledgeBaseTaskQueueService, KnowledgeBaseTaskQueueService>();

            // 注册 LLM 服务工厂
            services.AddSingleton<ILlmServiceFactory, LlmServiceFactory>();

            // 注册后台服务
            services.AddSingleton<KnowledgeBaseBackgroundService>();
            services.AddHostedService<KnowledgeBaseBackgroundService>();

            // 注册 Python 运行时
            services.AddPythonRuntime(configuration);

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
