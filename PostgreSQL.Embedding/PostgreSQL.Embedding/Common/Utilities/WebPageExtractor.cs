
using AngleSharp;
using HtmlAgilityPack;

namespace PostgreSQL.Embedding.Utils
{
    public static class WebPageExtractor
    {
        public static async Task<WebPageExtractionResult> ExtractWebPageAsync(string url, string contentSelector)
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(30);
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                    var response = await httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    var html = await response.Content.ReadAsStringAsync();

                    var config = Configuration.Default.WithDefaultLoader();
                    var context = BrowsingContext.New(config);
                    var document = await context.OpenAsync(request => request.Content(html));

                    var fetchResult = new WebPageExtractionResult()
                    {
                        Url = url,
                        Metadata = new Dictionary<string, string>()
                    };

                    // 提取标题
                    var eleTitle = document.QuerySelector("title");
                    if (eleTitle != null)
                        fetchResult.Title = eleTitle.TextContent;

                    // 提取 meta 标签作为元数据
                    var metaNodes = document.QuerySelectorAll("meta");
                    foreach (var meta in metaNodes)
                    {
                        var name = meta.GetAttribute("name") ?? meta.GetAttribute("property") ?? meta.GetAttribute("itemprop");
                        var content = meta.GetAttribute("content");

                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(content))
                        {
                            fetchResult.Metadata[name] = content;
                        }
                    }

                    var eleContent = document.QuerySelector(contentSelector ?? "body");
                    if (eleContent != null)
                        fetchResult.Content = eleContent.TextContent;

                    return fetchResult;
                }
            }
            catch (HttpRequestException e)
            {
                throw new ArgumentException("请检查地址是否正确");
            }
        }

    }

    public class WebPageExtractionResult
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}
