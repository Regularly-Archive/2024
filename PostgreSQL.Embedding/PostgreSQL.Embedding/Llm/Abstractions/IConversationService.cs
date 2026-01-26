


using PostgreSQL.Embedding.Domain.Models;

namespace PostgreSQL.Embedding.Llm.Abstractions
{
    public interface IConversationService
    {
        Task InvokeAsync(ConversationRequestModel model, long appId, HttpContext HttpContext, CancellationToken cancellationToken = default);
    }
}
