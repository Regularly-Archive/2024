using LLama.Common;
using Microsoft.AspNetCore.Http;
using PostgreSQL.Embedding.Common.Models;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface ILlmChatService
    {
        Task<string> ChatAsync(OpenAIModel request);

        IAsyncEnumerable<string> ChatStreamAsync(OpenAIModel request);
    }
}
