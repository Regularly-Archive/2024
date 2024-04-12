using Microsoft.AspNetCore.Mvc.RazorPages;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.Common.Models.KernelMemory;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IKnowledgeBaseService
    {
        Task<KnowledgeBase> CreateKnowledgeBase(KnowledgeBase knowledgeBase);
        Task UpdateKnowledgeBase(KnowledgeBase knowledgeBase);
        Task ImportKnowledgeFromFiles(string taskId, long knowledgeBaseId, IEnumerable<string> files);
        Task ImportKnowledgeFromUrl(string taskId, long knowledgeBaseId, string url);
        Task ImportKnowledgeFromText(string taskId, long knowledgeBaseId, string text);
        Task DeleteKnowledgeBaseChunksById(long knowledgeBaseId);
        Task DeleteKnowledgeBaseChunksByFileName(long knowledgeBaseId, string fileName);
        Task<PageResult<KMPartition>> GetKnowledgeBaseChunks(long knowledgeBaseId, string fileName = null, int pageIndex = 1, int pageSize = 10);
        Task<KMSearchResult> SearchAsync(long knowledgeBaseId, string question, double minRelevance = 0, int limit = 5);
        Task<KMAskResult> AskAsync(long knowledgeBaseId, string question, double minRelevance = 0.75);
        Task<bool> IsDocumentReady(long knowledgeBaseId, string fileName);
        Task HandleImportingQueueAsync(int batchLimit = 5);
    }
}
