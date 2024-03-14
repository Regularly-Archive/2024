using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.Common.Models.KernelMemory;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IKnowledgeBaseService
    {
        Task<KnowledgeBase> CreateKnowledgeBase(KnowledgeBase knowledgeBase);
        Task UpdateKnowledgeBase(KnowledgeBase knowledgeBase);
        Task ImportKnowledgeFromFiles(string taskId, long knowledgeBaseId, IEnumerable<string> files);
        Task ImportKnowledgeFromUrl(string taskId, long knowledgeBaseId, string url);
        Task DeleteKnowledgesById(long knowledgeBaseId);
        Task DeleteKnowledgesByFileName(long knowledgeBaseId, string fileName);
        Task<List<KMPartition>> GetKnowledgeBaseDetails(long knowledgeBaseId, string fileName = null);
        Task<KMSearchResult> SearchAsync(long knowledgeBaseId, string question, double minRelevance = 0, int limit = 5);
        Task<KMAskResult> AskAsync(long knowledgeBaseId, string question, double minRelevance = 0.75);
        Task<bool> IsDocumentReady(long knowledgeBaseId, string fileName);
    }
}
