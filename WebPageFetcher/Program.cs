using AngleSharp;
using AngleSharp.Dom;
using HtmlAgilityPack;
using System.Net;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;

public class Program
{
    private static void ExtractFromRSS(string url)
    {
        using (XmlReader reader = XmlReader.Create(url))
        {
            try
            {
                SyndicationFeed feed = SyndicationFeed.Load(reader);

                Console.WriteLine("Feed Title: " + feed.Title.Text);
                Console.WriteLine("Feed Links:");

                foreach (SyndicationLink link in feed.Links)
                {
                    Console.WriteLine("  " + link.Uri);
                }

                Console.WriteLine("Items:");

                foreach (SyndicationItem item in feed.Items)
                {
                    Console.WriteLine("  Title: " + item.Title.Text);
                    Console.WriteLine("  Link: " + item.Links.FirstOrDefault()?.Uri);
                    if (item.Content != null)
                    {
                        Console.WriteLine("  Content: " + (item.Content as TextSyndicationContent)!.Text);
                    }
                    if (item.Summary != null)
                    {
                        Console.WriteLine("  Summary: " + item.Summary.Text);
                    }
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                // 如果解析过程中出现错误，打印错误信息
                Console.WriteLine("Error reading the feed: " + ex.Message);
            }
        }
    }

    private static async Task<List<string>> ExtractFromSitemap(string url)
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync(url);

        var urls = new List<string>();

        var doc = new XmlDocument();
        doc.LoadXml(await response.Content.ReadAsStringAsync());

        foreach (XmlNode itemNode in doc.SelectNodes("//url")!)
        {
            var linkNode = itemNode.SelectSingleNode("loc");
            if (linkNode != null)
                urls.Add(linkNode.InnerText);
        }

        return urls;
    }

    public static async Task GetWebPageContentAsync(string url, string contentSelector)
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
                var htmlDocument = new HtmlDocument();

                var eleTitle = document.QuerySelector("title");
                Console.WriteLine("Title: " + eleTitle.TextContent);

                var eleContent = document.QuerySelector(contentSelector);
                Console.WriteLine("Content: " + CleanHtml(eleContent.Text()));
            }
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine("Error reading the content: " + ex.Message);
        }
    }

    public static async Task Main(string[] args)
    {
        ExtractFromRSS("https://www.ruanyifeng.com/blog/atom.xml");
        ExtractFromRSS("https://blog.yuanpei.me/index.xml");

        var urls2 = await ExtractFromSitemap("https://blog.yuanpei.me/sitemap.xml");

        await GetWebPageContentAsync("https://sspai.com/post/86171", ".article-detail");
        await GetWebPageContentAsync("https://blog.yuanpei.me/posts/the-boy-the-heron-the-self-reconciliation/", "article");
    }

    static string CleanHtml(string html)
    {
        var text = Regex.Replace(html, "<.*?>", string.Empty);
        text = Regex.Replace(text, "<script.*?script>", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<style.*?style>", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<!--.*?-->", string.Empty, RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(text);
        text = text.Trim();
        text = Regex.Replace(text, @"\s+", " ");
        return text;
    }
}

