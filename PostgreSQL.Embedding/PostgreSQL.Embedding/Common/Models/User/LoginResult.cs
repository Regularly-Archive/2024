using Newtonsoft.Json;

namespace PostgreSQL.Embedding.Common.Models.User
{
    public class LoginResult
    {

        [JsonProperty("accessToken")]
        public string AccessToken { get; set; }

        [JsonProperty("userInfo")]
        public UserInfo UserInfo {  get; set; }
    }

    public class UserInfo
    {
        public string Id { get; set; }
        public string UserName { get; set; }
    }
}
