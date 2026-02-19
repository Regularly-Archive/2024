using Microsoft.Extensions.DependencyInjection;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Core;
using PostgreSQL.Embedding.Llm.Services.Rerank;
using PostgreSQL.Embedding.Llm.Services.Retrieval;

namespace PostgreSQL.Embedding.Llm.Services
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加检索服务
        /// </summary>
        public static IServiceCollection AddRetrievalServices(this IServiceCollection services)
        {
            services.AddScoped<IKnowledgeRetrievalService, VectorsRetrievalService>();
            services.AddScoped<IKnowledgeRetrievalService, FullTextRetrievalService>();
            services.AddScoped<WebSearchRetrievalService>();

            return services;
        }

        /// <summary>
        /// 添加重排序服务
        /// </summary>
        public static IServiceCollection AddRerankServices(this IServiceCollection services)
        {
            services.AddKeyedSingleton<IRerankService, BgeRerankService>(nameof(RerankerType.BGE));
            services.AddKeyedSingleton<IRerankService, BM25RerankerService>(nameof(RerankerType.BM25));
            services.AddKeyedSingleton<IRerankService, FlashRerankService>(nameof(RerankerType.FlashRank));

            return services;
        }

        /// <summary>
        /// 添加引用服务
        /// </summary>
        public static IServiceCollection AddCitationServices(this IServiceCollection services)
        {
            services.AddScoped<CitationService>();

            return services;
        }

        /// <summary>
        /// 添加所有 LLM 服务
        /// </summary>
        public static IServiceCollection AddLlmServices(this IServiceCollection services)
        {
            services.AddRetrievalServices();
            services.AddRerankServices();
            services.AddCitationServices();

            return services;
        }
    }
}
