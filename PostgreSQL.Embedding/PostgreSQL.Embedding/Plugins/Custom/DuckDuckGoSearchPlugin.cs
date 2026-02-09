using AngleSharp;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Domain.Models.Search;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Net;
using System.Text.Json;

namespace PostgreSQL.Embedding.Plugins.Custom
{
    [KernelPlugin(Description = "DuckDuckGo 搜索引擎。隐私保护的网页搜索服务，提供 Instant Answer API 和 HTML 搜索两种方式。结果会通过 Artifacts 事件展示。", Version = "1.1")]
    public class DuckDuckGoSearchPlugin : BasePlugin, ISearchEngine
    {
        // DuckDuckGo HTML results have multiple classes: "result results_links results_links_deep web-result"
        // Use [class~="result"] to match elements with "result" in their class attribute
        private const string SELECTOR_TAG_ITEM = "[class~='result']";
        private const string SELECTOR_TAG_ITEM_TITLE = ".result__title";
        private const string SELECTOR_TAG_LINK = "a";
        private const string SELECTOR_TAG_HREF = "href";
        private const string SELECTOR_TAG_ITEM_DESC = ".result__snippet";
        private const string SELECTOR_TAG_ITEM_URL = ".result__url";

        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpClientFactory _httpClientFactory;

        public DuckDuckGoSearchPlugin(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory)
            : base(serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("使用 DuckDuckGo Instant Answer API 搜索关键词，返回搜索结果（稳定性高，不易被识别为爬虫）。")]
        public async Task<SearchResult> SearchAsync(
            [Description("搜索关键词")] string keyword,
            [Description("最大返回结果数量，默认为 10")] int limit = 10,
            [Description("要搜索的特定域名或网站")] string filterDomain = "")
        {
            // 使用 DuckDuckGo Instant Answer API，不会被检测为爬虫
            return await SearchWithApiAsync(keyword, limit, filterDomain);
        }

        public async Task<SearchResult> SearchWithApiAsync(
            [Description("搜索关键词")] string keyword,
            [Description("最大返回结果数量，默认为 10")] int limit = 10,
            [Description("要搜索的特定域名或网站")] string filterDomain = "")
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            // DuckDuckGo Instant Answer API
            var apiUrl = $"https://api.duckduckgo.com/?q={WebUtility.UrlEncode(keyword)}&format=json&no_html=1&skip_disambig=1";

            try
            {
                var response = await httpClient.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var searchResult = await ExtractFromApi(keyword, responseBody, limit, filterDomain);
                return searchResult;
            }
            catch (Exception ex)
            {
                return new SearchResult() { Keyword = keyword };
            }
        }

        private async Task<SearchResult> ExtractFromApi(string keyword, string jsonResponse, int limit, string filterDomain)
        {
            var searchResult = new SearchResult() { Keyword = keyword };

            try
            {
                using var doc = JsonDocument.Parse(jsonResponse);
                var root = doc.RootElement;

                // 提取 Related Topics (主要搜索结果)
                if (root.TryGetProperty("RelatedTopics", out var relatedTopics))
                {
                    var entries = new List<Entry>();

                    foreach (var topic in relatedTopics.EnumerateArray())
                    {
                        if (topic.TryGetProperty("FirstURL", out var urlElement) &&
                            topic.TryGetProperty("Text", out var textElement))
                        {
                            var url = urlElement.GetString();
                            var text = textElement.GetString();

                            // 过滤域名
                            if (!string.IsNullOrEmpty(filterDomain) &&
                                !url.Contains(filterDomain, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            // 提取标题（从 URL 中获取）
                            var title = ExtractTitleFromUrl(url);

                            entries.Add(new Entry()
                            {
                                Title = title,
                                Url = url,
                                Snippet = text ?? string.Empty
                            });

                            if (entries.Count >= limit)
                                break;
                        }
                    }

                    searchResult.Entries = entries;
                }

                // 如果 RelatedTopics 为空，尝试提取 Abstract
                if (searchResult.Entries.Count == 0)
                {
                    if (root.TryGetProperty("Abstract", out var abstractElement))
                    {
                        var abstractText = abstractElement.GetString();
                        if (root.TryGetProperty("AbstractURL", out var abstractUrl))
                        {
                            var abstractUrlStr = abstractUrl.GetString();
                            searchResult.Entries.Add(new Entry()
                            {
                                Title = keyword,
                                Url = abstractUrlStr ?? string.Empty,
                                Snippet = abstractText ?? string.Empty
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 解析失败返回空结果
            }

            return searchResult;
        }

        private string ExtractTitleFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;

            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath;

                // 从路径中提取标题，通常是 /wiki/Title 或类似的格式
                var segments = path.Split('/');
                if (segments.Length > 0)
                {
                    var lastSegment = segments[segments.Length - 1];
                    // URL 解码
                    return Uri.UnescapeDataString(lastSegment)
                        .Replace("-", " ")
                        .Replace("_", " ");
                }
            }
            catch
            {
            }

            return url;
        }

        public async Task<SearchResult> SearchWithHtmlAsync(
            [Description("关键词")] string keyword,
            int limit = 30,
            string filterDomain = "",
            string region = "us-en")
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0");
            httpClient.DefaultRequestHeaders.Referrer = new Uri("https://html.duckduckgo.com/html/");

            var query = string.IsNullOrEmpty(filterDomain) ? keyword : $"site:{filterDomain} {keyword}";
            var searchUrl = $"https://html.duckduckgo.com/html/?kl={region}&q={WebUtility.UrlEncode(query)}";

            var searchResult = await GetAsync(httpClient, keyword, searchUrl);

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

            return searchResult;
        }

        private async Task<SearchResult> GetAsync(HttpClient httpClient, string keyword, string url)
        {
            try
            {
                HttpResponseMessage response;

                // Check if this is a pagination request (contains pagination params)
                if (url.Contains("&s=") && url.Contains("&vqd="))
                {
                    // POST request for pagination
                    var queryParams = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
                    var content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("q", keyword),
                        new KeyValuePair<string, string>("s", queryParams["s"] ?? "0"),
                        new KeyValuePair<string, string>("kl", queryParams["kl"] ?? "us-en"),
                        new KeyValuePair<string, string>("vqd", queryParams["vqd"] ?? "")
                    });

                    response = await httpClient.PostAsync("https://html.duckduckgo.com/html/", content);
                }
                else
                {
                    // GET request for initial search
                    response = await httpClient.GetAsync(url);
                }

                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var searchResult = await ExtractSearchResults(keyword, responseBody);

                return searchResult;
            }
            catch (HttpRequestException ex)
            {
                return new SearchResult() { Keyword = keyword };
            }
        }

        private async Task<SearchResult> ExtractSearchResults(string query, string html)
        {
            var searchResult = new SearchResult() { Keyword = query };

            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(request => request.Content(html));

            var eleItems = document.QuerySelectorAll(SELECTOR_TAG_ITEM);
            if (eleItems == null || !eleItems.Any()) return searchResult;

            searchResult.Entries = eleItems
                .Select(x =>
                {
                    var eleTitle = x.QuerySelector(SELECTOR_TAG_ITEM_TITLE);
                    var eleLink = eleTitle?.QuerySelector(SELECTOR_TAG_LINK);
                    var eleUrl = x.QuerySelector(SELECTOR_TAG_ITEM_URL);
                    var eleSnippet = x.QuerySelector(SELECTOR_TAG_ITEM_DESC);

                    // DuckDuckGo URL is wrapped in a redirect link, extract the actual URL from the href
                    var href = eleLink?.Attributes[SELECTOR_TAG_HREF]?.Value;
                    var url = ExtractUrlFromRedirect(href);

                    return new Entry()
                    {
                        Title = eleTitle?.TextContent?.Trim() ?? string.Empty,
                        Url = url,
                        Snippet = eleSnippet?.TextContent?.Trim() ?? string.Empty
                    };
                })
                .Where(x => !string.IsNullOrEmpty(x.Title) && !string.IsNullOrEmpty(x.Url))
                .ToList();

            // Extract pagination info (DuckDuckGo uses POST for pagination)
            var nextForm = document.QuerySelector(".nav-link form");
            if (nextForm != null)
            {
                var sValue = nextForm.QuerySelector("input[name='s']")?.GetAttribute("value");
                var vqdValue = nextForm.QuerySelector("input[name='vqd']")?.GetAttribute("value");
                var klValue = nextForm.QuerySelector("input[name='kl']")?.GetAttribute("value");

                if (!string.IsNullOrEmpty(sValue) && int.TryParse(sValue, out var startOffset))
                {
                    // Store pagination info for next request
                    searchResult.NextPage = $"?q={WebUtility.UrlEncode(query)}&s={startOffset}&kl={klValue ?? "us-en"}&vqd={vqdValue}";
                    searchResult.HasNextPage = true;
                }
            }

            return searchResult;
        }

        /// <summary>
        /// Extract the vqd token from HTML for POST pagination requests
        /// </summary>
        private string ExtractVqdToken(string html)
        {
            try
            {
                var config = Configuration.Default.WithDefaultLoader();
                var context = BrowsingContext.New(config);
                var document = context.OpenAsync(request => request.Content(html)).Result;

                var vqdInput = document.QuerySelector("input[name='vqd']");
                return vqdInput?.GetAttribute("value") ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// DuckDuckGo uses redirect URLs like: https://duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2F
        /// Extract the actual URL from the redirect
        /// </summary>
        private string ExtractUrlFromRedirect(string redirectUrl)
        {
            if (string.IsNullOrEmpty(redirectUrl)) return redirectUrl;

            try
            {
                var uri = new Uri(redirectUrl);
                var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);

                // The actual URL is in the "uddg" parameter
                var uddg = queryParams["uddg"];
                if (!string.IsNullOrEmpty(uddg))
                {
                    return WebUtility.UrlDecode(uddg);
                }
            }
            catch
            {
                // If parsing fails, return the original URL
            }

            return redirectUrl;
        }
    }
}
