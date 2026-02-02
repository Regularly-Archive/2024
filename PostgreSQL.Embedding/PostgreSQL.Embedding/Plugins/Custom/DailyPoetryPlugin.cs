using Microsoft.SemanticKernel;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.Custom
{
    [KernelPlugin(Description = "今日诗词插件。调用今日诗词 API，随机返回一首古诗词（包含标题、作者、正文、朝代等）。", Version = "1.1")]
    public class DailyPoetryPlugin : BasePlugin
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public DailyPoetryPlugin(IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("随机获取一首古诗词。返回诗词的标题、作者、朝代和正文内容。")]
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
