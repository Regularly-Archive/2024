using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.KernelMemory;
using PostgreSQL.Embedding.Domain.Models.WebApi;

namespace PostgreSQL.Embedding.Llm.Abstractions
{
    public interface IKnowledgeBaseService
    {
        Task<KnowledgeBase> CreateKnowledgeBase(KnowledgeBase knowledgeBase);
        Task UpdateKnowledgeBase(KnowledgeBase knowledgeBase);
        Task ImportKnowledgeFromFiles(string taskId, long knowledgeBaseId, IEnumerable<string> files);
        Task ImportKnowledgeFromUrl(string taskId, long knowledgeBaseId, string url, int urltype, string contentSelector);
        Task ImportKnowledgeFromText(string taskId, long knowledgeBaseId, string text);
        Task DeleteKnowledgeBaseChunksById(long knowledgeBaseId);
        Task DeleteKnowledgeBaseChunksByFileName(long knowledgeBaseId, string fileName);
        Task<PagedResult<KMPartition>> GetKnowledgeBaseChunks(long knowledgeBaseId, string fileName = null, int pageIndex = 1, int pageSize = 10);
        Task<KMSearchResult> SearchAsync(long knowledgeBaseId, string question, RetrievalType retrievalType = RetrievalType.Mixed, double minRelevance = 0, int limit = 5);
        Task<KMAskResult> AskAsync(long knowledgeBaseId, string question, RetrievalType retrievalType = RetrievalType.Mixed, double minRelevance = 0, int limit = 5);
        Task<bool> IsDocumentReady(long knowledgeBaseId, string fileName);
        Task ReImportKnowledges(long knowledgeBaseId, string fileName = null);
        Task<KMPartition> GetKnowledgeBaseChunk(long knowledgeBaseId, string fileId, string partId);
    }
}
