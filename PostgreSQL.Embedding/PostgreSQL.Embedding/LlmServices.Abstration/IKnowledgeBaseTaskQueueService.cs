namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IKnowledgeBaseTaskQueueService
    {
        Task FetchAsync(int batchLimit = 5);
    }
}
