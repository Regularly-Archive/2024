using Microsoft.SemanticKernel;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models.Plugin;
using PostgreSQL.Embedding.Domain.Models.Search;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace PostgreSQL.Embedding.Plugins.Custom
{
    [KernelPlugin(Description = "Tavily 搜索引擎服务。一个专为 AI 智能体设计的搜索引擎，支持高级搜索深度和答案提取。", Version = "1.0")]
    public class TavilySearchPlugin : BasePlugin, ISearchEngine
    {
        [PluginParameter(Description = "Tavily API Key，可在 tavily.com 申请")] string API_KEY { get; set; }

        private readonly IHttpClientFactory _httpClientFactory;

        public TavilySearchPlugin(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory)
            : base(serviceProvider)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("使用 Tavily 搜索引擎搜索关键词，返回网页结果列表（标题、URL、摘要）。支持指定返回数量、域名筛选和时间范围。")]
        public async Task<SearchResult> SearchAsync(
            [Description("搜索关键词")] string keyword,
            [Description("最大返回结果数量，默认为 5")] int limit = 5,
            [Description("要搜索的特定域名或网站，如：zhihu.com，表示只搜索该站点的内容")] string include_domain = "")
        {
            if (!Validate(out var errorMessages)) throw new Exception(string.Join("", errorMessages));

            using var httpClient = _httpClientFactory.CreateClient();

            var requestBody = new Dictionary<string, object>
            {
                { "query", keyword },
                { "include_answer", "advanced" },
                { "search_depth", "advanced" },
                { "max_results", limit }
            };

            if (!string.IsNullOrEmpty(include_domain))
            {
                requestBody["include_domains"] = new[] { include_domain };
            }

            var jsonContent = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(requestBody),
                System.Text.Encoding.UTF8,
                "application/json"
            );

            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", API_KEY);

            var response = await httpClient.PostAsync("https://api.tavily.com/search", jsonContent);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            return ExtractSearchResult(keyword, content);
        }

        private SearchResult ExtractSearchResult(string keyword, string content)
        {
            var searchResult = new SearchResult() { Keyword = keyword };
            var jObject = JObject.Parse(content);

            // 提取答案（如果存在）
            var answer = jObject["answer"]?.Value<string>();
            if (!string.IsNullOrEmpty(answer))
            {
                searchResult.Entries.Add(new Entry
                {
                    Title = "AI_Summary",
                    Url = "",
                    Snippet = answer,
                    Relevance = 1.0f
                });
            }

            // 提取搜索结果
            var results = jObject["results"]?.Value<JArray>();
            if (results != null && results.Count > 0)
            {
                var entries = results.Select(x => new Entry
                {
                    Url = x["url"]?.Value<string>() ?? "",
                    Title = x["title"]?.Value<string>() ?? "",
                    Snippet = x["content"]?.Value<string>() ?? "",
                    Relevance = x["score"]?.Value<float>() ?? 0
                });

                searchResult.Entries.AddRange(entries);
            }

            return searchResult;
        }
    }
}
