using PostgreSQL.Embedding.Common.Models;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IConversationService
    {
        Task Invoke(OpenAIModel model, string sk, HttpContext HttpContext);
    }
}
