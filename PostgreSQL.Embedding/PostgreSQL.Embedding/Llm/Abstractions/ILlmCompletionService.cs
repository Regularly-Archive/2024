using PostgreSQL.Embedding.Domain.Models;

namespace PostgreSQL.Embedding.Llm.Abstractions
{
    public interface ILlmCompletionService
    {
        public Task<string> CompletionAsync(OpenAICompletionModel request);
    }
}
