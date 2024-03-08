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
    }
}
