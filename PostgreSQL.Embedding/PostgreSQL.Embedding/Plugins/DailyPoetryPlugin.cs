using Microsoft.SemanticKernel;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common.Attributes;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "今日诗词插件")]
    public class DailyPoetryPlugin
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public DailyPoetryPlugin(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("随机获取诗词")]
        public async Task<string> GenerateAsync()
        {
            using var httpClient = _httpClientFactory.CreateClient();
            var token = await GetToken(httpClient);

            httpClient.DefaultRequestHeaders.Add("X-User-Token", token);
            return await httpClient.GetStringAsync("https://v2.jinrishici.com/sentence");
        }

        private async Task<string> GetToken(HttpClient httpClient)
        {
            var response = await httpClient.GetStringAsync("https://v2.jinrishici.com/token");
            return JObject.Parse(response)["data"].Value<string>();   
        }
    }
}
