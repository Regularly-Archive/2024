using PostgreSQL.Embedding.Common.Models;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IChatService
    {
        Task Chat(OpenAIModel model, string sk, HttpContext HttpContext);
    }
}
