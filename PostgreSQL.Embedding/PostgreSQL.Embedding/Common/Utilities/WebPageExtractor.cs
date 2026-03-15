
using AngleSharp;
using HtmlAgilityPack;

namespace PostgreSQL.Embedding.Utils
{
    public static class WebPageExtractor
    {
        private static readonly HashSet<string> TextContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "text/plain",
            "text/markdown",
            "text/x-markdown",
            "text/css",
            "text/csv",
            "application/json",
            "text/json",
            "application/xml",
            "text/xml",
            "application/javascript",
            "text/javascript"
        };

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

                    var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                    var html = await response.Content.ReadAsStringAsync();

                    var fetchResult = new WebPageExtractionResult
                    {
                        Url = url,
                        Metadata = new Dictionary<string, string>()
                    };

                    // 根据 Content-Type 判断是 HTML 还是纯文本
                    if (IsHtmlContent(contentType))
                    {
                        return await ParseHtmlAsync(url, html, contentSelector, fetchResult);
                    }
                    else
                    {
                        return ParseTextContent(url, html, fetchResult);
                    }
                }
            }
            catch (HttpRequestException)
            {
                throw new ArgumentException("请检查地址是否正确");
            }
        }

        private static bool IsHtmlContent(string contentType)
        {
            // 明确是 HTML 或者没有明确类型时默认当作 HTML 处理
            if (string.IsNullOrEmpty(contentType))
                return true;

            return contentType.Equals("text/html", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<WebPageExtractionResult> ParseHtmlAsync(string url, string html, string contentSelector, WebPageExtractionResult fetchResult)
        {
            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(request => request.Content(html));

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

        private static WebPageExtractionResult ParseTextContent(string url, string content, WebPageExtractionResult fetchResult)
        {
            // 纯文本直接返回内容，标题尝试从 URL 推断
            fetchResult.Content = content;
            fetchResult.Title = ExtractTitleFromUrl(url);
            return fetchResult;
        }

        private static string ExtractTitleFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath;
                var fileName = Path.GetFileNameWithoutExtension(path);
                return string.IsNullOrEmpty(fileName) ? null : fileName;
            }
            catch
            {
                return null;
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
