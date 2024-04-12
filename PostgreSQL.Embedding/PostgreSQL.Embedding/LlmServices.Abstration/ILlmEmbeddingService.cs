using PostgreSQL.Embedding.Common.Models;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface ILlmEmbeddingService
    {
        Task<List<float>> Embedding(OpenAIEmbeddingModel embeddingModel);
    }
}
