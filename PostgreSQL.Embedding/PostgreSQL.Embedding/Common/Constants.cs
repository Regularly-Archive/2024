using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace PostgreSQL.Embedding.Common
{
    public class Constants
    {
        public const string Admin = "admin";
        public const string HttpRequestHeader_Provider = "x-wikit-llm-provider";
        public const string HttpRequestHeader_ConversationId = "x-wikit-conversation-id";
        public const string HttpRequestHeader_ConversationName = "x-wikit-conversation-name";
        public const string HttpRequestHeader_ConversationFlag = "x-wikit-conversation-flag";
        public const string HttpResponseHeader_ReferenceMessageId = "x-wikit-message-reference-id";
        public const string DefaultUploadFolder = "Upload";
        public const string DefaultEmptyAnswer = "抱歉，我无法回答你的问题！";
        public const int DefaultRetrievalLimit = 5;
        public const decimal DefaultRetrievalRelevance = 0M;
        public const string DefaultErrorAnswer = "抱歉，服务器开小差了！";
    }
}
