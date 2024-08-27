using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Models.Search;
using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "一个基于 JinaAI 的插件，支持信息检索及信息提取等功能")]
    public sealed class JinaAIPlugin : ISearchEngine
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public JinaAIPlugin(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("一个由 JinaAI 驱动的搜索接口，返回内容格式为 JSON。")]
        public async Task<string> SearchAsync([Description("查询关键词")] string keyword)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            var searchEnginePayload = await httpClient.GetFromJsonAsync<JinaAISearchResult>($"https://s.jina.ai/{keyword}");

            var searchResults = new SearchResult() { Query = keyword };
            searchResults.Entries = searchEnginePayload.Data.Select(x => new Entry()
            {
                Url = x.Url,
                Title = x.Title,
                Description = x.Description,
            })
            .ToList();

            return JsonConvert.SerializeObject(searchResults);
        }

        [KernelFunction]
        [Description("一个由 JinaAI 驱动的信息提取接口，可以返回指定网页的内容，返回格式为 Markdown。")]
        public async Task<string> ExtractAsync([Description("网址")] string url)
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
