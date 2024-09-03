using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Models.Plugin;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "FireCrawl 网络爬虫插件")]
    public class FireCrawlPlugin : BasePlugin
    {
        [PluginParameter(Description = "FireCrawl 的 API KEY", Required = true)]
        public string API_KEY { get; set; }

        private readonly IHttpClientFactory _httpClientFactory;
        public FireCrawlPlugin(IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("一个由 FireCraw 驱动的信息提取接口，可以返回指定网页的内容，返回格式为 Markdown。")]
        public async Task<string> ScrapeAsync([Description("地址")] string url)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {API_KEY}");

            var payload = new { url = url, formats = new List<string>() { "markdown" } };
            var content = JsonContent.Create<dynamic>(payload);

            var response = await httpClient.PostAsync("https://api.firecrawl.dev/v1/scrape", content);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }
    }
}
