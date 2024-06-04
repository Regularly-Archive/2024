using DocumentFormat.OpenXml.Office.CustomUI;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Net;
using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;

namespace PostgreSQL.Embedding.Utils
{
    public static class RSSExtractor
    {
        public static Task<List<WebPageExtractionResult>> ExtractAsync(string url)
        {
            using var reader = XmlReader.Create(url);
            var feed = SyndicationFeed.Load(reader);
            foreach (var item in feed.Items)
            {
                if (item.Content != null)
                {
                    var content = (item.Content as TextSyndicationContent).Text;
                    var cleanedContent = CleanHtml(content);
                    item.Content = new TextSyndicationContent(cleanedContent);
                }
                if (item.Summary != null)
                {
                    var summary = item.Summary.Text;
                    var cleanedSummary = CleanHtml(summary);
                    item.Summary = new TextSyndicationContent(cleanedSummary);
                }
            }

            var extractionResults = feed.Items.Select(item =>
            {
                return new WebPageExtractionResult()
                {
                    Url = item.Links.FirstOrDefault().Uri.ToString(),
                    Title = item.Title.Text,
                    Content = item.Content != null ?
                        (item.Content as TextSyndicationContent).Text :
                        item.Summary.Text,
                };
            })
            .ToList();

            return Task.FromResult(extractionResults);
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
}
