using AngleSharp;
using HtmlAgilityPack;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using System.ComponentModel;
using System.Text;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "微软必应搜索插件")]
    public class BingSearchPlugin
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public BingSearchPlugin(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("使用关键词进行检索")]
        public async Task<string> Search([Description("关键词")] string keyword)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0");
            httpClient.DefaultRequestHeaders.Referrer = new Uri("https://cn.bing.com/");

            var response = await httpClient.GetAsync($"https://bing.com/search?q={keyword}");
            response.EnsureSuccessStatusCode();


            var html = await response.Content.ReadAsStringAsync();
            var searchResult = await ExtractSearchResults(keyword, html);
            if (!searchResult.Entries.Any())
            {
                var content = await new JinaAIPlugin(_httpClientFactory).ExtractAsync($"https://bing.com/search?q={keyword}");
                return content;
            }

            return searchResult.ToString();
        }

        private async Task<SearchResult> ExtractSearchResults(string query, string html)
        {
            var seachResult = new SearchResult() { Query = query };

            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(request => request.Content(html));

            var eleMain = document.QuerySelector("main");
            if (eleMain == null) return seachResult;

            var eleItems = eleMain.QuerySelectorAll(".b_algo");
            if (eleItems == null || !eleItems.Any()) return seachResult;

            seachResult.Entries = eleItems.Select(x =>
            {
                var eleTitle = x.QuerySelector("h2");
                return new Entry()
                {
                    Title = eleTitle.TextContent,
                    Url = eleTitle.QuerySelector("a").Attributes["href"].Value,
                    Description = x.QuerySelector(".b_caption").TextContent
                };
            })
            .ToList();

            return seachResult;
        }
    }

    internal class SearchResult
    {
        public List<Entry> Entries { get; set; } = new List<Entry>();
        public string Query { get; set; }

        public override string ToString()
        {
            var stringBuilder = new StringBuilder();

            foreach (var entry in Entries)
            {
                stringBuilder.AppendLine($"Url: {entry.Url}");
                stringBuilder.AppendLine($"Title: {entry.Title}");
                stringBuilder.AppendLine($"Description: {entry.Description}");
                stringBuilder.AppendLine();
            }

            return stringBuilder.ToString();
        }
    }

    public class Entry
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
    }
}
