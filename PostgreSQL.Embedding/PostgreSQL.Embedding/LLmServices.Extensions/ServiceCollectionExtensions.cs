using Azure.AI.OpenAI;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.LlmServices.HuggingFace;
using PostgreSQL.Embedding.LlmServices.LLama;
using PostgreSQL.Embedding.LlmServices.Ollama;

namespace PostgreSQL.Embedding.LLmServices.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLLama(this IServiceCollection services)
        {
            return services.AddScoped<LLamaService>();
        }

        public static IServiceCollection AddHuggingFace(this IServiceCollection services)
        {
            return services.AddSingleton<HuggingFaceService>();
        }

        public static IServiceCollection AddOllama(this IServiceCollection services) 
        {
            return services.AddSingleton<OllamaService>();
        }
    }
}
