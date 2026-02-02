using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models.Search;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace PostgreSQL.Embedding.Plugins.Custom
{
    [KernelPlugin(Description = "JinaAI 服务插件。提供网页搜索（返回 JSON 格式结果）和网页内容提取（返回 Markdown 格式）两种能力。", Version = "1.1")]
    public sealed class JinaAIPlugin : BasePlugin, ISearchEngine
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public JinaAIPlugin(IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("使用 JinaAI 搜索接口查询关键词，返回 JSON 格式的搜索结果（标题、URL、摘要）。")]
        public async Task<SearchResult> SearchAsync(
            [Description("搜索关键词")] string keyword,
            [Description("最大返回结果数量，默认为 30")] int limit = 30,
            [Description("要搜索的特定域名或网站")] string filterDomain = "")
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            var searchEnginePayload = await httpClient.GetFromJsonAsync<JinaAISearchResult>($"https://s.jina.ai/{keyword}");

            var searchResults = new SearchResult() { Keyword = keyword };
            searchResults.Entries = searchEnginePayload.Data.Select(x => new Entry()
            {
                Url = x.Url,
                Title = x.Title,
                Snippet = x.Description,
            })
            .ToList();

            return searchResults;
        }

        [KernelFunction]
        [Description("使用 JinaAI 提取指定网页的全文内容，返回 Markdown 格式。可用于获取搜索结果页面的详细内容。")]
        public async Task<string> ExtractAsync([Description("要提取内容的网页 URL")] string url)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            return await httpClient.GetStringAsync($"https://r.jina.ai/{url}");
        }

        internal class JinaAISearchResult
        {
            [JsonPropertyName("code")]
            public int Code { get; set; }

            [JsonPropertyName("status")]
            public long Status { get; set; }

            [JsonPropertyName("data")]
            public List<JinaAISearchResultEntry> Data { get; set; }
        }

        internal class JinaAISearchResultEntry
        {
            [JsonPropertyName("title")]
            public string Title { get; set; }

            [JsonPropertyName("url")]
            public string Url { get; set; }

            [JsonPropertyName("content")]
            public string Content { get; set; }

            [JsonPropertyName("description")]
            public string Description { get; set; }
        }
    }
}
