namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface ILlmEmbeddingService
    {
        Task<List<float>> Embedding(string text);
    }
}
