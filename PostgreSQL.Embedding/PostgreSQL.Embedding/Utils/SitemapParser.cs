using System.Xml;

namespace PostgreSQL.Embedding.Utils
{
    public class SitemapParser
    {
        public static async Task<IEnumerable<SitemapEntry>> ParseSitemap(string url)
        {
            using var client = new HttpClient();
            string content = await client.GetStringAsync(url);

            var document = new XmlDocument();
            document.LoadXml(content);

            var namespaceManager = new XmlNamespaceManager(document.NameTable);
            namespaceManager.AddNamespace("sm", "http://www.sitemaps.org/schemas/sitemap/0.9");

            var urlNodes = document.SelectNodes("//sm:url", namespaceManager);

            var entries = new List<SitemapEntry>();
            foreach (XmlNode urlNode in urlNodes)
            {
                
                var loc = urlNode.SelectSingleNode("sm:loc", namespaceManager)?.InnerText;
                var lastmod = urlNode.SelectSingleNode("sm:lastmod", namespaceManager)?.InnerText;
                entries.Add(new SitemapEntry() { Url = loc, LastModified = lastmod });
            }

            return entries;
        }

        public class SitemapEntry
        {
            public string Url { get; set; }
            public string LastModified { get; set; }
        }
    }
}
