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
        public const string DefaultUploadFolder = "Upload";
    }
}
