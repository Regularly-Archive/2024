
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
                    var response = await httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    var html = await response.Content.ReadAsStringAsync();

                    var config = Configuration.Default.WithDefaultLoader();
                    var context = BrowsingContext.New(config);
                    var document = await context.OpenAsync(request => request.Content(html));

                    var fetchResult = new WebPageExtractionResult() { Url = url };

                    var eleTitle = document.QuerySelector("title");
                    if (eleTitle != null)
                        fetchResult.Title = eleTitle.TextContent;

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
    }
}
