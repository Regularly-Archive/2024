﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace PostgreSQL.Embedding.Common
{
    public class Constants
    {
        public const string Admin = "admin";
        public const string HttpRequestHeader_Provider = "X-LLM-SERVICE-PROVIDER";
        public const string HttpRequestHeader_ConversationId = "x-wikit-conversation-id";
    }
}
