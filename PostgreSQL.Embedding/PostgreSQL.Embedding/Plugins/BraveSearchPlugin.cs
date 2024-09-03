using AngleSharp;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Models.Search;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "Brave 搜索插件")]
    public class BraveSearchPlugin : BasePlugin, ISearchEngine
    {
        private const string SELECTOR_RESULTS = "#results";
        private const string SELECTOR_RESULTS_ITEM = ".snippet";
        private const string SELECTOR_RESULTS_ITEM_DESCRIPTION = ".snippet-description";
        private const string SELECTOR_RESULTS_ITEM_Title = ".title";

        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpClientFactory _httpClientFactory;

        public BraveSearchPlugin(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory)
            : base(serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("使用关键词进行检索")]
        public async Task<string> SearchAsync([Description("关键词")] string keyword)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0");
            httpClient.DefaultRequestHeaders.Referrer = new Uri("https://search.brave.com");

            var response = await httpClient.GetAsync($"https://search.brave.com/search?q={keyword}&offset=1&spellcheck=0");
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync();
            var searchResult = await ExtractSearchResults(keyword, html);
            return JsonConvert.SerializeObject(searchResult);
        }

        private async Task<SearchResult> ExtractSearchResults(string query, string html)
        {
            var seachResult = new SearchResult() { Query = query };

            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(request => request.Content(html));

            var eleMain = document.QuerySelector(SELECTOR_RESULTS);
            if (eleMain == null) return seachResult;

            var eleItems = eleMain.QuerySelectorAll(SELECTOR_RESULTS_ITEM);
            if (eleItems == null || !eleItems.Any()) return seachResult;

            seachResult.Entries = eleItems.Select(x =>
            {
                if (x.Id == "pagination-snippet") return null;

                return new Entry()
                {
                    Title = x.QuerySelector("a")?.QuerySelector(SELECTOR_RESULTS_ITEM_Title)?.TextContent,
                    Url = x.QuerySelector("a")?.Attributes["href"]?.Value,
                    Description = x.QuerySelector(SELECTOR_RESULTS_ITEM_DESCRIPTION)?.TextContent
                };
            })
            .Where(x => x != null)
            .ToList();

            return seachResult;
        }
    }
}
