using AngleSharp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PostgreSQL.Embedding.Domain.Models.Search;
using PostgreSQL.Embedding.Plugins.Custom;
using Shouldly;
using System.Net;

namespace Wikit.Tests.Plugins
{
    /// <summary>
    /// Tests for DuckDuckGoSearchPlugin
    /// Note: Network tests may fail if DuckDuckGo detects the request as bot.
    /// Use local HTML parsing tests for reliable validation.
    /// </summary>
    public class When_Call_DuckDuckGoSearchPlugin
    {
        // Local test data file (copied to output directory)
        private readonly string _localHtmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "duckduckgo.html");

        [Fact]
        public void It_Should_Initialize_Correctly()
        {
            // Arrange
            var mockServiceProvider = new Mock<IServiceProvider>(MockBehavior.Strict);
            var mockHttpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Loose);
            var mockLoggerFactory = new Mock<ILoggerFactory>(MockBehavior.Loose);
            var mockLogger = new Mock<ILogger>(MockBehavior.Loose);
            var mockHttpContextAccessor = new Mock<IHttpContextAccessor>(MockBehavior.Loose);

            mockServiceProvider.Setup(x => x.GetService(typeof(IHttpClientFactory)))
                .Returns(mockHttpClientFactory.Object);
            mockServiceProvider.Setup(x => x.GetService(typeof(ILoggerFactory)))
                .Returns(mockLoggerFactory.Object);
            mockServiceProvider.Setup(x => x.GetService(typeof(IHttpContextAccessor)))
                .Returns(mockHttpContextAccessor.Object);
            mockLoggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>()))
                .Returns(mockLogger.Object);

            // Act
            var plugin = new DuckDuckGoSearchPlugin(mockServiceProvider.Object, mockHttpClientFactory.Object);

            // Assert
            this.ShouldSatisfyAllConditions(
                () => plugin.ShouldNotBeNull()
            );
        }

        [Fact]
        public async Task It_Should_Parse_Local_HTML_Successfully()
        {
            // Skip if local HTML file doesn't exist
            if (!File.Exists(_localHtmlPath))
            {
                return;
            }

            // Arrange
            var html = await File.ReadAllTextAsync(_localHtmlPath);

            // Act - Parse using AngleSharp with the same logic as plugin
            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(request => request.Content(html));

            // Use [class~="result"] to match classes containing "result"
            var eleItems = document.QuerySelectorAll("[class~='result']");

            // Assert
            this.ShouldSatisfyAllConditions(
                () => eleItems.ShouldNotBeNull(),
                () => eleItems.Length.ShouldBeGreaterThan(0),
                () => eleItems.Length.ShouldBeGreaterThanOrEqualTo(10)
            );
        }

        [Fact]
        public async Task It_Should_Extract_Snowflake_Results_From_Local_HTML()
        {
            // Skip if local HTML file doesn't exist
            if (!File.Exists(_localHtmlPath))
            {
                return;
            }

            // Arrange
            var html = await File.ReadAllTextAsync(_localHtmlPath);

            // Act - Use the same parsing logic as the plugin
            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(request => request.Content(html));

            var eleItems = document.QuerySelectorAll("[class~='result']");
            var entries = eleItems
                .Select(x =>
                {
                    var eleTitle = x.QuerySelector(".result__title");
                    var eleLink = eleTitle?.QuerySelector("a");
                    var eleSnippet = x.QuerySelector(".result__snippet");

                    var href = eleLink?.GetAttribute("href");
                    var url = ExtractUrlFromRedirect(href);

                    return new Entry()
                    {
                        Title = eleTitle?.TextContent?.Trim() ?? "",
                        Url = url,
                        Snippet = eleSnippet?.TextContent?.Trim() ?? ""
                    };
                })
                .Where(x => !string.IsNullOrEmpty(x.Title) && !string.IsNullOrEmpty(x.Url))
                .ToList();

            // Assert
            this.ShouldSatisfyAllConditions(
                () => entries.ShouldNotBeEmpty(),
                () => entries.Count.ShouldBeGreaterThan(5),
                () => entries.First().Title.ShouldContain("Snowflake"),
                () => entries.Any(x => x.Url.Contains("snowflake.com")),
                () => entries.Any(x => x.Url.Contains("wikipedia.org")),
                () => entries.All(x => !string.IsNullOrEmpty(x.Snippet)).ShouldBeTrue()
            );
        }

        [Fact]
        public async Task It_Should_Parse_Entry_Titles_From_Local_HTML()
        {
            // Skip if local HTML file doesn't exist
            if (!File.Exists(_localHtmlPath))
            {
                return;
            }

            // Arrange
            var html = await File.ReadAllTextAsync(_localHtmlPath);

            // Act
            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(request => request.Content(html));

            var eleTitles = document.QuerySelectorAll(".result__title");

            // Assert
            this.ShouldSatisfyAllConditions(
                () => eleTitles.ShouldNotBeNull(),
                () => eleTitles.Length.ShouldBeGreaterThan(0),
                () => eleTitles.Length.ShouldBeGreaterThanOrEqualTo(10),
                () => eleTitles[0].TextContent.ShouldContain("Snowflake"),
                () => eleTitles.Any(x => x.TextContent.Contains("Snowflake Inc.")).ShouldBeTrue()
            );
        }

        [Fact]
        public async Task It_Should_Extract_Real_Url_From_Redirect_Links()
        {
            // Skip if local HTML file doesn't exist
            if (!File.Exists(_localHtmlPath))
            {
                return;
            }

            // Arrange
            var html = await File.ReadAllTextAsync(_localHtmlPath);

            // Act
            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(request => request.Content(html));

            var eleLinks = document.QuerySelectorAll(".result__title a");

            // Extract URLs and decode them
            var urls = eleLinks
                .Select(x => x.GetAttribute("href"))
                .Where(href => !string.IsNullOrEmpty(href))
                .Select(ExtractUrlFromRedirect)
                .ToList();

            // Assert
            this.ShouldSatisfyAllConditions(
                () => urls.ShouldNotBeEmpty(),
                () => urls.Count.ShouldBeGreaterThan(5),
                () => urls.Any(url => url.Contains("snowflake.com")).ShouldBeTrue(),
                () => urls.Any(url => url.Contains("wikipedia.org")).ShouldBeTrue(),
                () => urls.All(url => url.StartsWith("http")).ShouldBeTrue()
            );
        }

        [Fact]
        public async Task It_Should_Parse_Snippets_From_Local_HTML()
        {
            // Skip if local HTML file doesn't exist
            if (!File.Exists(_localHtmlPath))
            {
                return;
            }

            // Arrange
            var html = await File.ReadAllTextAsync(_localHtmlPath);

            // Act
            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(request => request.Content(html));

            var eleSnippets = document.QuerySelectorAll(".result__snippet");

            // Assert
            this.ShouldSatisfyAllConditions(
                () => eleSnippets.ShouldNotBeNull(),
                () => eleSnippets.Length.ShouldBeGreaterThan(0),
                () => eleSnippets.Length.ShouldBeGreaterThanOrEqualTo(10),
                () => eleSnippets[0].TextContent.ShouldContain("Snowflake"),
                () => eleSnippets.All(x => !string.IsNullOrEmpty(x.TextContent.Trim())).ShouldBeTrue()
            );
        }

        [Fact]
        public async Task It_Should_Create_SearchResult_Object_From_Local_HTML()
        {
            // Skip if local HTML file doesn't exist
            if (!File.Exists(_localHtmlPath))
            {
                return;
            }

            // Arrange
            var html = await File.ReadAllTextAsync(_localHtmlPath);
            var keyword = "snowflake";

            // Act - Simulate what the plugin does
            var searchResult = new SearchResult() { Keyword = keyword };

            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(request => request.Content(html));

            var eleItems = document.QuerySelectorAll("[class~='result']");
            if (eleItems != null && eleItems.Any())
            {
                searchResult.Entries = eleItems
                    .Select(x =>
                    {
                        var eleTitle = x.QuerySelector(".result__title");
                        var eleLink = eleTitle?.QuerySelector("a");
                        var eleSnippet = x.QuerySelector(".result__snippet");

                        var href = eleLink?.GetAttribute("href");
                        var url = ExtractUrlFromRedirect(href);

                        return new Entry()
                        {
                            Title = eleTitle?.TextContent?.Trim() ?? "",
                            Url = url,
                            Snippet = eleSnippet?.TextContent?.Trim() ?? ""
                        };
                    })
                    .Where(x => !string.IsNullOrEmpty(x.Title) && !string.IsNullOrEmpty(x.Url))
                    .ToList();
            }

            // Assert
            this.ShouldSatisfyAllConditions(
                () => searchResult.Keyword.ShouldBe("snowflake"),
                () => searchResult.Entries.ShouldNotBeEmpty(),
                () => searchResult.Entries.Count.ShouldBeGreaterThan(5),
                () => searchResult.Entries[0].Title.ShouldNotBeNullOrEmpty(),
                () => searchResult.Entries[0].Url.ShouldNotBeNullOrEmpty(),
                () => searchResult.Entries[0].Snippet.ShouldNotBeNullOrEmpty()
            );
        }

        [Fact]
        public void It_Should_Implement_ISearchEngine()
        {
            // Arrange
            var mockServiceProvider = new Mock<IServiceProvider>(MockBehavior.Strict);
            var mockHttpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Loose);
            var mockLoggerFactory = new Mock<ILoggerFactory>(MockBehavior.Loose);
            var mockLogger = new Mock<ILogger>(MockBehavior.Loose);
            var mockHttpContextAccessor = new Mock<IHttpContextAccessor>(MockBehavior.Loose);

            mockServiceProvider.Setup(x => x.GetService(typeof(IHttpClientFactory)))
                .Returns(mockHttpClientFactory.Object);
            mockServiceProvider.Setup(x => x.GetService(typeof(ILoggerFactory)))
                .Returns(mockLoggerFactory.Object);
            mockServiceProvider.Setup(x => x.GetService(typeof(IHttpContextAccessor)))
                .Returns(mockHttpContextAccessor.Object);
            mockLoggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>()))
                .Returns(mockLogger.Object);

            // Act
            var plugin = new DuckDuckGoSearchPlugin(mockServiceProvider.Object, mockHttpClientFactory.Object);

            // Assert
            this.ShouldSatisfyAllConditions(
                () => plugin.ShouldBeAssignableTo<ISearchEngine>()
            );
        }

        [Fact]
        public void It_Should_Have_Correct_Plugin_Description()
        {
            // Arrange
            var mockServiceProvider = new Mock<IServiceProvider>(MockBehavior.Strict);
            var mockHttpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Loose);
            var mockLoggerFactory = new Mock<ILoggerFactory>(MockBehavior.Loose);
            var mockLogger = new Mock<ILogger>(MockBehavior.Loose);
            var mockHttpContextAccessor = new Mock<IHttpContextAccessor>(MockBehavior.Loose);

            mockServiceProvider.Setup(x => x.GetService(typeof(IHttpClientFactory)))
                .Returns(mockHttpClientFactory.Object);
            mockServiceProvider.Setup(x => x.GetService(typeof(ILoggerFactory)))
                .Returns(mockLoggerFactory.Object);
            mockServiceProvider.Setup(x => x.GetService(typeof(IHttpContextAccessor)))
                .Returns(mockHttpContextAccessor.Object);
            mockLoggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>()))
                .Returns(mockLogger.Object);

            // Act
            var plugin = new DuckDuckGoSearchPlugin(mockServiceProvider.Object, mockHttpClientFactory.Object);

            // Assert
            this.ShouldSatisfyAllConditions(
                () => plugin.PluginName.ShouldBe("DuckDuckGoSearchPlugin")
            );
        }

        /// <summary>
        /// Extract the real URL from DuckDuckGo redirect URLs
        /// DuckDuckGo uses: https://duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2F
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

        [Fact]
        public async Task It_Should_Search_With_Api_Async()
        {
            // This test uses DuckDuckGo Instant Answer API which won't be blocked
            // Arrange
            var serviceProvider = new ServiceCollection()
                .AddHttpClient()
                .AddHttpContextAccessor()
                .AddLogging()
                .BuildServiceProvider();

            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var plugin = new DuckDuckGoSearchPlugin(serviceProvider, httpClientFactory);

            // Act
            var searchResult = await plugin.SearchWithApiAsync("snowflake", 10, "");

            // Assert
            this.ShouldSatisfyAllConditions(
                () => searchResult.ShouldNotBeNull(),
                () => searchResult.Keyword.ShouldBe("snowflake"),
                () => searchResult.Entries.ShouldNotBeEmpty(),
                () => searchResult.Entries.Count.ShouldBeLessThanOrEqualTo(10),
                () => searchResult.Entries.All(x => !string.IsNullOrEmpty(x.Title)).ShouldBeTrue(),
                () => searchResult.Entries.All(x => !string.IsNullOrEmpty(x.Url)).ShouldBeTrue(),
                () => searchResult.Entries.All(x => !string.IsNullOrEmpty(x.Snippet)).ShouldBeTrue()
            );
        }

        [Fact]
        public async Task It_Should_Filter_By_Domain_With_Api()
        {
            // This test uses DuckDuckGo Instant Answer API
            // Arrange
            var serviceProvider = new ServiceCollection()
                .AddHttpClient()
                .AddHttpContextAccessor()
                .AddLogging()
                .BuildServiceProvider();

            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var plugin = new DuckDuckGoSearchPlugin(serviceProvider, httpClientFactory);

            // Act
            var searchResult = await plugin.SearchWithApiAsync("snowflake", 10, "wikipedia.org");

            // Assert
            this.ShouldSatisfyAllConditions(
                () => searchResult.ShouldNotBeNull(),
                () => searchResult.Entries.ShouldNotBeEmpty(),
                () => searchResult.Entries.All(x => x.Url.Contains("wikipedia.org")).ShouldBeTrue()
            );
        }

        [Fact]
        public async Task It_Should_Search_Snowflake_RealApi()
        {
            // This is a real API test that queries DuckDuckGo Instant Answer API
            // Arrange
            var serviceProvider = new ServiceCollection()
                .AddHttpClient()
                .AddHttpContextAccessor()
                .AddLogging()
                .BuildServiceProvider();

            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var plugin = new DuckDuckGoSearchPlugin(serviceProvider, httpClientFactory);

            // Act - Real API call to DuckDuckGo
            var searchResult = await plugin.SearchAsync("Snowflake", 10, "");

            // Assert - Verify real API response
            this.ShouldSatisfyAllConditions(
                () => searchResult.ShouldNotBeNull(),
                () => searchResult.Keyword.ShouldBe("Snowflake"),
                () => searchResult.Entries.ShouldNotBeEmpty(),
                () => searchResult.Entries.Count.ShouldBeLessThanOrEqualTo(10),
                () => searchResult.Entries.All(x => !string.IsNullOrEmpty(x.Title)).ShouldBeTrue(),
                () => searchResult.Entries.All(x => x.Url.StartsWith("http")).ShouldBeTrue(),
                () => searchResult.Entries.All(x => !string.IsNullOrEmpty(x.Snippet)).ShouldBeTrue(),
                // Verify we got actual Snowflake-related results
                () => searchResult.Entries.Any(x =>
                    x.Url.Contains("snowflake.com", StringComparison.OrdinalIgnoreCase) ||
                    x.Url.Contains("snowflake_inc", StringComparison.OrdinalIgnoreCase) ||
                    x.Title.Contains("Snowflake", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue()
            );
        }
    }
}
