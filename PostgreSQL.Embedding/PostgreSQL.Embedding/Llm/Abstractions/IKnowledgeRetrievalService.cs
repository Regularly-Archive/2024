using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Domain.Models.KernelMemory;

namespace PostgreSQL.Embedding.Llm.Abstractions
{
    public interface IKnowledgeRetrievalService
    {
        RetrievalType RetrievalType { get; }
        Task<KMSearchResult> SearchAsync(long knowledgeBaseId, string question, double minRelevance = 0, int limit = 5);
    }
}
