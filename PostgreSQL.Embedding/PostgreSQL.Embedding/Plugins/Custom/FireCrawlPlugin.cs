using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models.Plugin;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.Custom
{
    [KernelPlugin(Description = "FireCrawl 网页抓取插件。智能爬取网页并提取内容，返回格式化的 Markdown。能够处理动态网页和反爬措施。", Version = "1.1")]
    public class FireCrawlPlugin : BasePlugin
    {
        [PluginParameter(Description = "FireCrawl API Key，可在 firecrawl.dev 申请")] string API_KEY { get; set; }

        private readonly IHttpClientFactory _httpClientFactory;
        public FireCrawlPlugin(IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("使用 FireCrawl 抓取指定网页的正文内容，返回 Markdown 格式。可处理动态渲染页面和需要 JavaScript 执行的网站。")]
        public async Task<string> ScrapeAsync([Description("要抓取的网页 URL")] string url)
        {
            if (!Validate(out var errorMessages)) throw new Exception(string.Join("", errorMessages));

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
