using Microsoft.SemanticKernel;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models.Plugin;
using PostgreSQL.Embedding.Domain.Models.Search;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.Custom
{
    [KernelPlugin(Description = "博查（BoCha）Web Search API。提供高质量的中文网页搜索服务，支持返回搜索结果摘要。", Version = "1.1")]
    public class BoChaAIPlugin : BasePlugin, ISearchEngine
    {
        [PluginParameter(Description = "博查 API Key，可在 bochaai.com 申请")] string API_KEY { get; set; } = "sk-c4633d65488442f6af7dbb2a731fcd09";

        private readonly IHttpClientFactory _httpClientFactory;
        public BoChaAIPlugin(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory)
            : base(serviceProvider)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("使用博查搜索引擎搜索关键词，返回网页结果列表（标题、URL、摘要）。")]
        public async Task<SearchResult> SearchAsync(
            [Description("搜索关键词")] string keyword,
            [Description("最大返回结果数量，默认为 30")] int limit = 30,
            [Description("要搜索的特定域名或网站")] string filterDomain = "")
        {
            if (!Validate(out var errorMessages)) throw new Exception(string.Join("", errorMessages));


            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {API_KEY}");

            var payload = new { query = keyword, freshness = "noLimit", summary = true, count = 8 };
            var content = JsonContent.Create<dynamic>(payload);

            var response = await httpClient.PostAsync("https://api.bochaai.com/v1/web-search", content);
            response.EnsureSuccessStatusCode();

            var searchResult = ExtractSearchResult(await response.Content.ReadAsStringAsync());
            searchResult.Keyword = keyword;

            return searchResult;
        }

        private SearchResult ExtractSearchResult(string content)
        {
            var jObject = JObject.Parse(content);
            if (jObject["code"].Value<int>() != 200) 
                return null;

            var values = jObject["data"]["webPages"]["value"].Value<JArray>();
            if (values == null || values.Count == 0) 
                return null;

            var entries = values.Select(x => new Entry
            {
                Url = x["url"].Value<string>(),
                Title = x["name"].Value<string>(),
                Snippet = x["snippet"].Value<string>()
            });

            return new SearchResult() { Entries = entries.ToList() };
        }
    }
}
