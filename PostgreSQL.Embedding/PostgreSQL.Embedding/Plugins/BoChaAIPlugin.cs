using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Models.Plugin;
using PostgreSQL.Embedding.Common.Models.Search;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "博查 Web Search API")]
    public class BoChaAIPlugin : BasePlugin, ISearchEngine
    {
        [PluginParameter(Description = "API Key", Required = true)]
        public string API_KEY { get; set; } = "sk-c4633d65488442f6af7dbb2a731fcd09";

        private readonly IHttpClientFactory _httpClientFactory;
        public BoChaAIPlugin(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory)
            : base(serviceProvider)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("从网络中搜索信息")]
        public async Task<string> SearchAsync([Description("关键词")] string keyword)
        {
            if (!Validate(out var errorMessages)) throw new Exception(string.Join("", errorMessages));


            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {API_KEY}");

            var payload = new { query = keyword, freshness = "noLimit", summary = true, count = 8 };
            var content = JsonContent.Create<dynamic>(payload);

            var response = await httpClient.PostAsync("https://api.bochaai.com/v1/web-search", content);
            response.EnsureSuccessStatusCode();

            var searchResult = ExtractSearchResult(await response.Content.ReadAsStringAsync());
            searchResult.Query = keyword;

            return JsonConvert.SerializeObject(searchResult);
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
                Description = x["snippet"].Value<string>()
            });

            return new SearchResult() { Entries = entries.ToList() };
        }
    }
}
