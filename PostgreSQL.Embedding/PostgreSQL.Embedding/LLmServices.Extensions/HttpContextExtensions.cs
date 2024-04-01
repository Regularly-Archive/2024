using PostgreSQL.Embedding.Common;
using System.Web;

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

        public static string GetConversationName(this HttpContext context)
        {
            var conversationName = context.Request.Headers[Constants.HttpRequestHeader_ConversationName].FirstOrDefault();
            if (string.IsNullOrEmpty(conversationName))
            {
                return string.Empty;
            }

            
            return HttpUtility.UrlDecode(conversationName);
        }
    }
}
