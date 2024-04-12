﻿using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace PostgreSQL.Embedding.Common.Models.User
{
    public class LoginResult
    {

        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("userInfo")]
        public UserInfo UserInfo {  get; set; }
    }

    public class UserInfo
    {
        public string Id { get; set; }

        public string UserName { get; set; }

        public List<string> Role { get; set; }
        public string Avatar { get; set; }
    }
}
