


using PostgreSQL.Embedding.Common.Streaming;
using PostgreSQL.Embedding.Domain.Models;

namespace PostgreSQL.Embedding.Llm.Abstractions
{
    public interface IConversationService
    {
        Task InvokeAsync(ConversationRequestModel model, long appId, HttpContext HttpContext, CancellationToken cancellationToken = default);

        /// <summary>
        /// V2 streaming chat with Anthropic-compatible SSE format.
        /// Returns IAsyncEnumerable{ISseEvent} for better client interoperability.
        /// </summary>
        IAsyncEnumerable<ISseEvent> InvokeStreamingV2Async(
            ConversationRequestModel model,
            long appId,
            string input,
            string? conversationId = null,
            CancellationToken cancellationToken = default);
    }
}
