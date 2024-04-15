using PostgreSQL.Embedding.Common.Models;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface ILlmCompletionService
    {
        public Task<string> CompletionAsync(OpenAICompletionModel request);
    }
}
