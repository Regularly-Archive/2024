using Azure.AI.OpenAI;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.Llm.Connectors.HuggingFace;
using PostgreSQL.Embedding.Llm.Connectors.LLama;
using PostgreSQL.Embedding.Llm.Connectors.Ollama;

namespace PostgreSQL.Embedding.Common.Extensions
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
