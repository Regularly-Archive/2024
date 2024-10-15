using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models.KernelMemory;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IKnowledgeRetrievalService
    {
        RetrievalType RetrievalType { get; }
        Task<KMSearchResult> SearchAsync(long knowledgeBaseId, string question, double minRelevance = 0, int limit = 5);
    }
}
