using AngleSharp;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Models.Search;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Net;

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
        public async Task<SearchResult> SearchAsync([Description("关键词")] string query, int limit = 30)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0");
            httpClient.DefaultRequestHeaders.Referrer = new Uri("https://search.brave.com");

            var searchResult = await GetAsync(httpClient, query, $"https://search.brave.com/search?q={WebUtility.UrlEncode(query)}&source=web");
            while (searchResult.HasNextPage && searchResult.Entries.Count < limit)
            {
                var newSearchResult = await GetAsync(httpClient, query, searchResult.NextPage);
                if (searchResult.Entries.Any())
                {
                    searchResult.Entries.AddRange(newSearchResult.Entries);
                    searchResult.NextPage = newSearchResult.NextPage;
                    searchResult.HasNextPage = newSearchResult.HasNextPage;
                }
            }

            return searchResult;
        }

        public async Task<SearchResult> GetAsync(HttpClient httpClient, string query, string url)
        {
            try
            {
                var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var searchResult = await ExtractSearchResults(query, responseBody);

                return searchResult;
            }
            catch (HttpRequestException ex)
            {
                return new SearchResult();
            }
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

            var elePag = eleMain.QuerySelector("#pagination");
            if (elePag != null)
            {
                var nextPage = elePag.QuerySelector("a");
                if (nextPage != null)
                {
                    seachResult.HasNextPage = true;
                    seachResult.NextPage = "https://search.brave.com" + nextPage.Attributes["href"].Value;
                }
            }

            seachResult.Entries = eleItems.Select(x =>
            {
                if (x.Id == "pagination-snippet") return null;

                return new Entry()
                {
                    Title = x.QuerySelector("a")?.QuerySelector(SELECTOR_RESULTS_ITEM_Title)?.TextContent,
                    Url = x.QuerySelector("a")?.Attributes["href"]?.Value,
                    Snippet = x.QuerySelector(SELECTOR_RESULTS_ITEM_DESCRIPTION)?.TextContent
                };
            })
            .Where(x => x != null && !string.IsNullOrEmpty(x.Title ) && !string.IsNullOrEmpty(x.Snippet))
            .ToList();

            return seachResult;
        }
    }
}
