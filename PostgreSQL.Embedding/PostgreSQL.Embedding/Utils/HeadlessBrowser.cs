using PuppeteerSharp;

namespace PostgreSQL.Embedding.Utils
{
    public class HeadlessBrowser
    {
        public async Task<string> FetchAsync(string url, string selector = "body")
        {
            await new BrowserFetcher().DownloadAsync().ConfigureAwait(false);
            using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                AcceptInsecureCerts = true,
                Args = [ "--no-sandbox", "--disable-web-security" ]
            }).ConfigureAwait(false);

            using var page = await browser.NewPageAsync().ConfigureAwait(false);

            var response = await page.GoToAsync(url).ConfigureAwait(false);
            await page.WaitForSelectorAsync(selector).ConfigureAwait(false);

            return await response.TextAsync().ConfigureAwait(false);
        }
    }
}
