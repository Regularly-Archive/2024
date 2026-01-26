namespace PostgreSQL.Embedding.Llm.Abstractions
{
    public interface IKnowledgeBaseTaskQueueService
    {
        Task FetchAsync(int batchLimit = 5);
    }
}
