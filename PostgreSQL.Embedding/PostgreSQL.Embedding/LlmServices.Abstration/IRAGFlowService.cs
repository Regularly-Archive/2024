using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Models.RAG;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IRAGFlowService
    {
        Task<List<LlmCitationModel>> RetrieveCitationsAsync(long appId, string question);
        Task<string> GenerateAnswerAsync(long appId, string conversationId, string question, List<LlmCitationModel> citations);
    }
}
