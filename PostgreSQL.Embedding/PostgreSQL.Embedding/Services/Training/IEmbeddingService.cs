namespace PostgreSQL.Embedding.Services.Training
{
    public interface IEmbeddingService
    {
        Task AddTextEmbeddingAsync(string text);
        Task AddFileEmbeddingAsync(string filePath);
        Task AddWebPageEmbeddingAsync(string url);
        Task SearchAsync(string query, int topK);
    }
}
