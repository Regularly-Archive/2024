using HtmlAgilityPack;

namespace PostgreSQL.Embedding.Utils
{
    public static class WebPageTitleFetcher
    {
        public static async Task<string> GetWebPageTitleAsync(string url)
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    var response = await httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    var htmlDocument = new HtmlDocument();
                    htmlDocument.LoadHtml(await response.Content.ReadAsStringAsync());

                    var titleNode = htmlDocument.DocumentNode.SelectSingleNode("//title");
                    if (titleNode != null)
                    {
                        return titleNode.InnerText;
                    }
                }
            }
            catch (HttpRequestException e)
            {
                return null;
            }

            return null;
        }
    }
}
