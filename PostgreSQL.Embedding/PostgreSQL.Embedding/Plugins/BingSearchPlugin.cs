using AngleSharp;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Domain.Models.Search;
using PostgreSQL.Embedding.Plugins.Abstration;
using SqlSugar;
using System.ComponentModel;
using System.Net;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "微软必应搜索插件")]
    public class BingSearchPlugin : BasePlugin, ISearchEngine
    {
        private const string SELECTOR_TAG_MAIN = "main";
        private const string SELECTOR_TAG_LINK = "a";
        private const string SELECTOR_TAG_ITEM = ".b_algo";
        private const string SELECTOR_TAG_ITEM_TITLE = "h2";
        private const string SELECTOR_TAG_HREF = "href";
        private const string SELECTOR_TAG_ITEM_DESC = ".b_caption";
        private const string SELECTOR_TAG_PAGINATION = ".b_pag";
        private const string SELECTOR_TAG_PAGINATION_NEXT = ".sw_next";

        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpClientFactory _httpClientFactory;

        public BingSearchPlugin(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory)
            : base(serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("使用关键词进行检索")]
        public async Task<SearchResult> SearchAsync([Description("关键词")] string keyword, int limit = 30, string filterDomain = "")
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0");
            httpClient.DefaultRequestHeaders.Referrer = new Uri("https://bing.com/");

            var query = string.IsNullOrEmpty(filterDomain) ? keyword : $"site:{filterDomain} {keyword}";
            var searchResult = await GetAsync(httpClient, keyword, $"https://bing.com/search?q={WebUtility.UrlEncode(query)}");
            while (searchResult.Entries.Count < limit && searchResult.HasNextPage)
            {
                var newSearchResult = await GetAsync(httpClient, keyword, searchResult.NextPage);
                if (newSearchResult.Entries.Any())
                {
                    searchResult.Entries.AddRange(newSearchResult.Entries);
                    searchResult.NextPage = newSearchResult.NextPage;
                    searchResult.HasNextPage = newSearchResult.HasNextPage;
                }
            }
            await SendArtifacts(searchResult);
            return searchResult;
        }

        private async Task<SearchResult> GetAsync(HttpClient httpClient, string keyword, string url)
        {
            try
            {
                var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var resonseBody = await response.Content.ReadAsStringAsync();
                var searchResult = await ExtractSearchResults(keyword, resonseBody);

                return searchResult;
            }
            catch (HttpRequestException ex)
            {
                return new SearchResult() { Keyword = keyword };
            }
        }

        private async Task<SearchResult> ExtractSearchResults(string query, string html)
        {
            var seachResult = new SearchResult() { Keyword = query };

            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(request => request.Content(html));

            var eleMain = document.QuerySelector(SELECTOR_TAG_MAIN);
            if (eleMain == null) return seachResult;

            var elePag = eleMain.QuerySelector(SELECTOR_TAG_PAGINATION);
            if (elePag != null)
            {
                var eleNextPage = elePag.QuerySelector(SELECTOR_TAG_PAGINATION_NEXT);
                if (eleNextPage != null)
                {
                    seachResult.HasNextPage = true;
                    seachResult.NextPage = "https://bing.com" + eleNextPage.ParentElement.Attributes[SELECTOR_TAG_HREF].Value;
                }
            }

            var eleItems = eleMain.QuerySelectorAll(SELECTOR_TAG_ITEM);
            if (eleItems == null || !eleItems.Any()) return seachResult;

            seachResult.Entries = eleItems.Select(x =>
            {
                var eleTitle = x.QuerySelector(SELECTOR_TAG_ITEM_TITLE);
                return new Entry()
                {
                    Title = eleTitle.TextContent,
                    Url = eleTitle.QuerySelector(SELECTOR_TAG_LINK)?.Attributes[SELECTOR_TAG_HREF].Value,
                    Snippet = x.QuerySelector(SELECTOR_TAG_ITEM_DESC)?.TextContent ?? string.Empty
                };
            })
            .Where(x => !string.IsNullOrEmpty(x.Title) && !string.IsNullOrEmpty(x.Snippet))
            .ToList();

            return seachResult;
        }

        private async Task SendArtifacts(SearchResult searchResult)
        {
            if (searchResult == null || !searchResult.Entries.Any()) return;

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

    public interface ISearchEngine
    {
        Task<SearchResult> SearchAsync(string keyword, int limit = 30, string filterDomain = "");
    }
}
