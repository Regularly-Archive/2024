
using Microsoft.SemanticKernel;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Domain.Models.Plugin;
using PostgreSQL.Embedding.Domain.Models.Search;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.Custom
{
    [KernelPlugin(Description = "SerpApi 搜索引擎服务。通过 Google 搜索返回结构化的搜索结果，结果会通过 Artifacts 事件展示。", Version = "1.1")]
    public class SerpApiPlugin : BasePlugin, ISearchEngine
    {
        [PluginParameter(Description = "SerpApi API Key，可在 serpapi.com 申请")] string API_KEY { get; set; }

        private readonly IHttpClientFactory _httpClientFactory;
        public SerpApiPlugin(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory)
            : base(serviceProvider)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("使用 SerpApi 通过 Google 搜索引擎搜索关键词，返回网页结果列表（标题、URL、摘要）。")]
        public async Task<SearchResult> SearchAsync(
            [Description("搜索关键词")] string keyword,
            [Description("最大返回结果数量，默认为 30")] int limit = 30,
            [Description("要搜索的特定域名或网站")] string filterDomain = "")
        {
            if (!Validate(out var errorMessages)) throw new Exception(string.Join("", errorMessages));

            var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetAsync($"https://serpapi.com/search?q={keyword}&engine=google&api_key={API_KEY}");
            response.EnsureSuccessStatusCode();

            var searchResult = ExtractSearchResult(await response.Content.ReadAsStringAsync());
            searchResult.Keyword = keyword;

            await SendArtifacts(searchResult);
            return searchResult;
        }

        private SearchResult ExtractSearchResult(string content)
        {
            var jObject = JObject.Parse(content);

            var values = jObject["organic_results"].Value<JArray>();
            if (values == null || values.Count == 0)
                return null;

            var entries = values.Select(x => new Entry
            {
                Url = x["link"].Value<string>(),
                Title = x["title"].Value<string>(),
                Snippet = x["snippet"].Value<string>()
            });

            return new SearchResult() { Entries = entries.ToList() };
        }

        private async Task SendArtifacts(SearchResult searchResult)
        {
            var artifact = new LlmArtifactResponseModel("搜索结果", ArtifactType.Search);
            var payloads = searchResult.Entries.Select(x => new
            {
                link = x.Url,
                title = x.Title,
                description = x.Snippet
            });
            artifact.SetData(payloads);
            await EmitArtifactsAsync(artifact);
        }
    }
}
