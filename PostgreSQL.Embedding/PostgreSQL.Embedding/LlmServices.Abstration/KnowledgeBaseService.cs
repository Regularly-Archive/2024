using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IKnowledgeBaseService
    {
        Task ImportKnowledgeFromFiles(string taskId, long knowledgeBaseId, IEnumerable<string> files);
        Task ImportKnowledgeFromUrl(string taskId, long knowledgeBaseId, string url);
        Task DeleteKnowledgeById(long knowledgeBaseId);
        Task DeleteKnowledgeByFileName(long knowledgeBaseId, string fileName);
        Task<List<KnowledgeDetail>> GetKnowledgeBaseDetails(long knowledgeBaseId);
    }
}
