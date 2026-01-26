using PostgreSQL.Embedding.Domain.Models;

namespace PostgreSQL.Embedding.Llm.Abstractions
{
    public interface ILlmChatService
    {
        Task<string> ChatAsync(OpenAIModel request);

        IAsyncEnumerable<string> ChatStreamAsync(OpenAIModel request);
    }
}
