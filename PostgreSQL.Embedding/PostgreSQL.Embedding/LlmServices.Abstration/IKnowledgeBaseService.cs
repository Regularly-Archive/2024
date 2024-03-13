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
        Task DeleteKnowledgeById(long knowledgeBaseId);
        Task DeleteKnowledgeByFileName(long knowledgeBaseId, string fileName);
        Task<List<KnowledgeDetail>> GetKnowledgeBaseDetails(long knowledgeBaseId);
        Task<KMSearchResult> SearchAsync(long knowledgeBaseId, string question, double minRelevance = 0, int limit = 5);
        Task<KMAskResult> AskAsync(long knowledgeBaseId, string question, double minRelevance = 0.75);
        Task<bool> IsDocumentReady(long knowledgeBaseId, string fileName);
        Task<bool> IsTaskReady(long knowledgeBaseId, string taskId);
    }
}
