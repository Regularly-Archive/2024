using PostgreSQL.Embedding.Domain.Models;

namespace PostgreSQL.Embedding.Llm.Abstractions
{
    public interface ILlmEmbeddingService
    {
        Task<List<float>> Embedding(OpenAIEmbeddingModel embeddingModel);
    }
}
