using System.Net;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Tools.BuiltIn;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tests.Tools;

public sealed class WebFetchToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldFallbackToMarkdown_ForUnknownHtmlFormat()
    {
        using var client = new HttpClient(new StaticResponseHandler(
            "<html><body><h1>Title</h1><p>Hello <strong>world</strong>.</p></body></html>",
            "text/html"));
        var tool = new WebFetchTool(client);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object>
            {
                ["url"] = "https://example.test/article",
                ["format"] = "unsupported"
            },
            new ToolExecutionContext { AgentId = "test", ToolCallId = "call-1" });

        Assert.False(result.IsError);
        var text = Assert.IsType<TextBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("# Title", text);
        Assert.Contains("Hello **world**.", text);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldExtractArticleAndExposeCuratedMetadata()
    {
        using var client = new HttpClient(new StaticResponseHandler("""
            <html>
              <head>
                <title>Example Article</title>
                <meta name="description" content="A concise summary." />
                <meta name="author" content="Ada Lovelace" />
                <meta property="article:published_time" content="2026-08-05" />
                <link rel="canonical" href="/articles/example" />
              </head>
              <body>
                <nav>Ignore this navigation</nav>
                <article><h1>Article Heading</h1><p>Read <a href="/docs">the docs</a>.</p></article>
                <footer>Ignore this footer</footer>
              </body>
            </html>
            """, "text/html"));
        var tool = new WebFetchTool(client);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object> { ["url"] = "https://example.test/page" },
            new ToolExecutionContext { AgentId = "test", ToolCallId = "call-2" });

        Assert.False(result.IsError);
        var text = Assert.IsType<TextBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("# Example Article", text);
        Assert.Contains("> A concise summary.", text);
        Assert.Contains("Source: https://example.test/articles/example", text);
        Assert.Contains("Published: 2026-08-05", text);
        Assert.Contains("Author: Ada Lovelace", text);
        Assert.Contains("# Article Heading", text);
        Assert.Contains("[the docs](https://example.test/docs)", text);
        Assert.DoesNotContain("Ignore this navigation", text);
        Assert.DoesNotContain("Ignore this footer", text);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPreserveTextBlocksAndRemoveInteractiveNoise()
    {
        using var client = new HttpClient(new StaticResponseHandler("""
            <html><body><article>
              <h1>Introduction</h1><p>First paragraph.</p><p>Second paragraph.</p>
              <button>Copy Markdown</button><input value="Search" />
              <details><summary>Table of contents</summary><p>Hidden navigation</p></details>
              <ul><li>First item</li><li>Second item</li></ul>
            </article></body></html>
            """, "text/html"));
        var tool = new WebFetchTool(client);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object> { ["url"] = "https://example.test/page", ["format"] = "text" },
            new ToolExecutionContext { AgentId = "test", ToolCallId = "call-3" });

        Assert.False(result.IsError);
        var text = Assert.IsType<TextBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("Introduction\nFirst paragraph.\nSecond paragraph.", text);
        Assert.Contains("First item\nSecond item", text);
        Assert.DoesNotContain("Copy Markdown", text);
        Assert.DoesNotContain("Search", text);
        Assert.DoesNotContain("Table of contents", text);
        Assert.DoesNotContain("Hidden navigation", text);
    }

    private sealed class StaticResponseHandler(string content, string mediaType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, mediaType)
            });
    }
}
