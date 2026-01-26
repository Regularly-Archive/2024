using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Domain.Models.KernelMemory;
using PostgreSQL.Embedding.Domain.Models.RAG;

namespace PostgreSQL.Embedding.Llm.Abstractions;

public interface IRAGFlowService
{
    Task<List<LlmCitationModel>> GenerateCitationsAsync(long appId, string question, bool enableWebSearch = false);
    Task<string> GenerateAnswerAsync(long appId, string conversationId, string question, List<LlmCitationModel> citations);
    Task<string> GenerateAnswerAsync(string question, List<LlmCitationModel> citations);
    List<KMPartition> RerankDocuments(string question, List<KMPartition> partitions, RerankerType rerankerType = RerankerType.BM25);
    Task<List<string>> RewriteQueryAsync(string question, int limit, Kernel kernel);
}
