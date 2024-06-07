using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IConversationService
    {
        Task Invoke(OpenAIModel model, long appId, HttpContext HttpContext);
    }
}
