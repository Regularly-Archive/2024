using AngleSharp;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models.Search;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Net;

namespace PostgreSQL.Embedding.Plugins.Custom
{
    [KernelPlugin(Description = "Brave 搜索引擎。隐私优先的网页搜索服务，通过关键词搜索返回结果列表，支持分页和域名过滤。", Version = "1.1")]
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
        [Description("使用 Brave 搜索引擎搜索关键词，返回网页结果列表（标题、URL、摘要）。支持指定返回数量限制和域名筛选（使用 site:xxx 语法）。")]
        public async Task<SearchResult> SearchAsync(
            [Description("搜索关键词")] string keyword,
            [Description("最大返回结果数量，默认为 30")] int limit = 30,
            [Description("要搜索的特定域名或网站，如：zhihu.com，表示只搜索该站点的内容")] string filterDomain = "")
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0");
            httpClient.DefaultRequestHeaders.Referrer = new Uri("https://search.brave.com");

            var query = string.IsNullOrEmpty(filterDomain) ? keyword : $"site:{filterDomain} {keyword}";
            var searchResult = await GetAsync(httpClient, keyword, $"https://search.brave.com/search?q={WebUtility.UrlEncode(query)}&source=web");
            while (searchResult.HasNextPage && searchResult.Entries.Count < limit)
            {
                var newSearchResult = await GetAsync(httpClient, keyword, searchResult.NextPage);
                if (newSearchResult.Entries.Any())
                {
                    searchResult.Entries.AddRange(newSearchResult.Entries);
                    searchResult.NextPage = newSearchResult.NextPage;
                    searchResult.HasNextPage = newSearchResult.HasNextPage;
                }
            }

            return searchResult;
        }

        public async Task<SearchResult> GetAsync(HttpClient httpClient, string keyword, string url)
        {
            try
            {
                var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var searchResult = await ExtractSearchResults(keyword, responseBody);

                return searchResult;
            }
            catch (HttpRequestException ex)
            {
                return new SearchResult() { Keyword = keyword};
            }
        }

        private async Task<SearchResult> ExtractSearchResults(string query, string html)
        {
            var seachResult = new SearchResult() { Keyword = query };

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
