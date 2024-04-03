using PostgreSQL.Embedding.Common.Models.KernelMemory;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IFullTextSearchService
    {
        Task<KMSearchResult> SearchAsync(long knowledgeBaseId, string question, double? minRelevance = 0, int? limit = 5);
        Task<KMAskResult> AskAsync(long knowledgeBaseId, string question, double? minRelevance = 0);
    }
}
