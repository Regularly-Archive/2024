using PostgreSQL.Embedding.Common;
using System.Web;

namespace PostgreSQL.Embedding.LLmServices.Extensions
{
    public static class HttpContextExtensions
    {
        public static string GetOrCreateConversationId(this HttpContext context)
        {
            var conversationId = context.Request.Headers[Constants.HttpRequestHeader_ConversationId].FirstOrDefault();
            if (string.IsNullOrEmpty(conversationId)) conversationId = Guid.NewGuid().ToString();

            return conversationId;
        }

        public static string GetConversationName(this HttpContext context)
        {
            var conversationName = context.Request.Headers[Constants.HttpRequestHeader_ConversationName].FirstOrDefault();
            if (string.IsNullOrEmpty(conversationName)) return "未命名会话";

            return HttpUtility.UrlDecode(conversationName);
        }
    }
}
