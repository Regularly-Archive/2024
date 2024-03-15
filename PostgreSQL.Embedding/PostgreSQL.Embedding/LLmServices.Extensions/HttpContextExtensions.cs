using PostgreSQL.Embedding.Common;

namespace PostgreSQL.Embedding.LLmServices.Extensions
{
    public static class HttpContextExtensions
    {
        public static string GetOrCreateConversationId(this HttpContext context)
        {
            var conversationId = context.Request.Headers[Constants.HttpRequestHeader_ConversationId].FirstOrDefault();
            if (string.IsNullOrEmpty(conversationId))
            {
                conversationId = Guid.NewGuid().ToString();
                context.Response.Cookies.Append(Constants.HttpRequestHeader_ConversationId, conversationId);
                context.Response.Headers[Constants.HttpRequestHeader_ConversationId] = conversationId;
            }

            return conversationId;
        }
    }
}
