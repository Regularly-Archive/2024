using AngleSharp;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models.Search;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Net;

namespace PostgreSQL.Embedding.Plugins.Custom
{
    [KernelPlugin(Description = "微信搜索插件")]
    public class WeiXinSearchPlugin : BasePlugin, ISearchEngine
    {
        private const string SELECTOR_RESULTS = ".news-list";
        private const string SELECTOR_RESULTS_ITEM = "li";
        private const string SELECTOR_RESULTS_ITEM_DESCRIPTION = "p";
        private const string SELECTOR_RESULTS_ITEM_Title = "h3";

        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpClientFactory _httpClientFactory;

        public WeiXinSearchPlugin(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory)
            : base(serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("使用关键词进行检索")]
        public async Task<SearchResult> SearchAsync([Description("关键词")] string query, int limit = 30, string filterDomain = "")
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0");
            httpClient.DefaultRequestHeaders.Referrer = new Uri("https://weixin.sogou.com");

            var searchResult = await GetAsync(httpClient, query, $"https://weixin.sogou.com/weixin?ie=utf8&s_from=input&_sug_=y&_sug_type_=&type=2&query={WebUtility.UrlEncode(query)}");
            while (searchResult.HasNextPage && searchResult.Entries.Count < limit)
            {
                var newSearchResult = await GetAsync(httpClient, query, searchResult.NextPage);
                if (newSearchResult.Entries.Any())
                {
                    searchResult.Entries.AddRange(newSearchResult.Entries);
                    searchResult.NextPage = newSearchResult.NextPage;
                    searchResult.HasNextPage = newSearchResult.HasNextPage;
                }
            }
            
            // Todo: 需要对 URL 进行解码
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
            var seachResult = new SearchResult() { Keyword = query };

            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(request => request.Content(html));

            var eleMain = document.QuerySelector(SELECTOR_RESULTS);
            if (eleMain == null) return seachResult;

            var eleItems = eleMain.QuerySelectorAll(SELECTOR_RESULTS_ITEM);
            if (eleItems == null || !eleItems.Any()) return seachResult;

            var elePag = document.QuerySelector("#pagebar_container");
            if (elePag != null)
            {
                var nextPage = elePag.QuerySelector("#sogou_next");
                if (nextPage != null)
                {
                    seachResult.HasNextPage = true;
                    seachResult.NextPage = "https://weixin.sogou.com/weixin" + nextPage.Attributes["href"].Value;
                }
            }

            seachResult.Entries = eleItems.Select(x =>
            {
                var eleTextBox = x.QuerySelector(".txt-box");
                return new Entry()
                {
                    Title = eleTextBox?.QuerySelector(SELECTOR_RESULTS_ITEM_Title)?.TextContent,
                    Url = "https://weixin.sogou.com" + eleTextBox?.QuerySelector(SELECTOR_RESULTS_ITEM_Title).QuerySelector("a")?.Attributes["href"]?.Value,
                    Snippet = eleTextBox.QuerySelector(SELECTOR_RESULTS_ITEM_DESCRIPTION)?.TextContent
                };
            })
            .Where(x => x != null && !string.IsNullOrEmpty(x.Title) && !string.IsNullOrEmpty(x.Snippet))
            .ToList();

            return seachResult;
        }
    }
}
