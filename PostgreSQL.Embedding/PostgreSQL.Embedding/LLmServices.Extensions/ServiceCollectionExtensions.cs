using Azure.AI.OpenAI;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.LlmServices.HuggingFace;
using PostgreSQL.Embedding.LlmServices.LLama;

namespace PostgreSQL.Embedding.LLmServices.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLLama(this IServiceCollection services)
        {
            return services
                .AddSingleton<LLamaChatService>()
                .AddSingleton<LLamaEmbeddingService>()
                .AddSingleton<LLamaService>();
        }

        public static IServiceCollection AddHuggingFace(this IServiceCollection services)
        {
            services
                .AddSingleton<HuggingFaceEmbeddingService>()
                .AddSingleton<HuggingFaceService>();

            return services;
        }
    }
}
