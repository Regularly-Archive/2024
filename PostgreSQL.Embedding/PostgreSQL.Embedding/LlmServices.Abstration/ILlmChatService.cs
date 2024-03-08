using LLama.Common;
using Microsoft.AspNetCore.Http;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface ILlmChatService
    {
        Task<string> ChatAsync(string input);

        IAsyncEnumerable<string> ChatStreamAsync(string input);
    }
}
