using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;
using ReverseMarkdown;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// Fetches web content and renders the extracted main content as HTML, text, or Markdown.
/// </summary>
public class WebFetchTool : ITool, IToolResultProjector
{
    private static readonly HttpClient DefaultHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly HttpClient _httpClient;

    public WebFetchTool(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? DefaultHttpClient;
    }

    public string Name => "web_fetch";

    public ToolDefinition Definition => new()
    {
        Name = Name,
        Description = "Fetch web page content. HTML pages are parsed to extract their main article content and concise page metadata. Returns Markdown by default; use format 'html', 'text', or 'markdown'. The format parameter only applies to HTML. Unknown values fall back to markdown.",
        Schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                url = new { type = "string", description = "The URL to fetch" },
                format = new
                {
                    type = "string",
                    description = "Output format for HTML content. 'html' returns the original page HTML, 'text' returns extracted plain text, and 'markdown' returns extracted structured markdown. Unknown values fall back to markdown. Ignored for non-HTML content types. Default: markdown"
                }
            },
            required = new[] { "url" }
        })
    };

    public async Task<ToolResult> ExecuteAsync(IDictionary<string, object> args, ToolExecutionContext context)
    {
        try
        {
            var arguments = new ToolArgumentReader(Definition.Schema, args);
            var url = arguments.GetString("url");
            var format = arguments.GetString("format", "markdown");

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return ToolResult.FromError($"Invalid URL: {url}");
            if (uri.Scheme is not ("http" or "https"))
                return ToolResult.FromError("Only HTTP and HTTPS URLs are supported.");

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; InsightaAI/1.0)");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.CancellationToken);

            if (!response.IsSuccessStatusCode)
                return ToolResult.FromError($"Failed to fetch URL: {response.StatusCode} {response.ReasonPhrase}");

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!IsTextualContentType(contentType))
                return ToolResult.FromError($"Unsupported content type: {contentType}. Only text-based content types are supported.");

            var content = await response.Content.ReadAsStringAsync(context.CancellationToken);
            if (!IsHtmlContentType(contentType))
                return ToolResult.FromText(FormatNonHtmlResponse(uri, contentType, content));

            var extraction = ExtractHtml(uri, content);
            var renderedContent = format switch
            {
                "html" => extraction.OriginalHtml,
                "text" => extraction.Text,
                _ => extraction.Markdown
            };

            return ToolResult.FromText(FormatHtmlResponse(extraction, renderedContent));
        }
        catch (TaskCanceledException)
        {
            return ToolResult.FromError("Request timed out after 30 seconds.");
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Failed to fetch URL: {ex.Message}");
        }
    }

    public ToolResultRetentionPolicy RetentionPolicy { get; } = new()
    {
        CanReplay = true,
        PreferPersistence = true,
        MinimumLevel = ToolResultRetentionLevel.Removed
    };

    public ToolResultProjection CreatePreview(ToolResult result, ToolResultProjectionContext context)
    {
        var text = result.Content.OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty;
        var preview = text[..Math.Min(5000, text.Length)];
        if (context.Artifact != null)
            preview += $"\n\n[Full output saved as artifact {context.Artifact.Id}: {context.Artifact.Path}]";
        return new ToolResultProjection
        {
            Content = [new TextBlock { Text = preview }],
            Level = ToolResultRetentionLevel.Preview
        };
    }

    public ToolResultProjection CreatePlaceholder(ToolResultProjectionContext context) => new()
    {
        Content = [new TextBlock { Text = DefaultToolResultProjector.CreatePlaceholderText(context) }],
        Level = ToolResultRetentionLevel.Placeholder
    };

    private static WebPageExtractionResult ExtractHtml(Uri sourceUrl, string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        var contentRoot = document.QuerySelector("article, main, [role='main']") ?? document.DocumentElement;
        if (contentRoot is null)
            return new WebPageExtractionResult(
                sourceUrl, sourceUrl, null, null, null, null,
                new Dictionary<string, string>(), html, string.Empty, string.Empty);

        RemoveNoiseAndResolveUrls(contentRoot, sourceUrl);
        var metadata = ExtractMetadata(document);
        var title = NormalizeInlineText(document.QuerySelector("title")?.TextContent);
        var canonicalUrl = ResolveUrl(sourceUrl, document.QuerySelector("link[rel='canonical']")?.GetAttribute("href")) ?? sourceUrl;
        var contentHtml = contentRoot.InnerHtml;

        return new WebPageExtractionResult(
            sourceUrl,
            canonicalUrl,
            title,
            GetMetadata(metadata, "description", "og:description", "twitter:description"),
            GetMetadata(metadata, "author", "article:author"),
            GetMetadata(metadata, "article:published_time", "date", "datepublished"),
            metadata,
            html,
            RenderText(contentRoot),
            new Converter().Convert(contentHtml).Trim());
    }

    private static void RemoveNoiseAndResolveUrls(IElement contentRoot, Uri sourceUrl)
    {
        foreach (var element in contentRoot.QuerySelectorAll(
            "head, script, style, noscript, svg, nav, aside, footer, form, button, details, summary, dialog, input, select, textarea, option").ToArray())
            element.Remove();

        foreach (var link in contentRoot.QuerySelectorAll("a[href]"))
        {
            var absoluteUrl = ResolveUrl(sourceUrl, link.GetAttribute("href"));
            if (absoluteUrl != null)
                link.SetAttribute("href", absoluteUrl.AbsoluteUri);
        }

        foreach (var image in contentRoot.QuerySelectorAll("img[src]"))
        {
            var absoluteUrl = ResolveUrl(sourceUrl, image.GetAttribute("src"));
            if (absoluteUrl != null)
                image.SetAttribute("src", absoluteUrl.AbsoluteUri);
        }
    }

    private static string RenderText(IElement contentRoot)
    {
        var builder = new StringBuilder();
        AppendText(contentRoot, builder);
        return NormalizeBlockText(builder.ToString());
    }

    private static void AppendText(INode node, StringBuilder builder)
    {
        if (node is IText text)
        {
            builder.Append(text.Data);
            return;
        }

        if (node is not IElement element)
            return;

        if (element.LocalName.Equals("br", StringComparison.OrdinalIgnoreCase))
        {
            AppendLineBreak(builder);
            return;
        }

        var isBlock = IsBlockTextElement(element.LocalName);
        if (isBlock)
            AppendLineBreak(builder);

        foreach (var child in element.ChildNodes)
            AppendText(child, builder);

        if (isBlock)
            AppendLineBreak(builder);
    }

    private static bool IsBlockTextElement(string tagName) => tagName.Equals("address", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("article", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("blockquote", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("div", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("dl", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("dt", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("dd", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("figcaption", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("figure", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("h1", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("h2", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("h3", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("h4", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("h5", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("h6", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("li", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("main", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("ol", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("p", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("pre", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("section", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("table", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("tr", StringComparison.OrdinalIgnoreCase)
        || tagName.Equals("ul", StringComparison.OrdinalIgnoreCase);

    private static void AppendLineBreak(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] != '\n')
            builder.AppendLine();
    }

    private static string NormalizeBlockText(string text)
    {
        var normalized = text.Replace("\r\n", "\n");
        normalized = Regex.Replace(normalized, "[^\\S\\r\\n]+", " ");
        normalized = Regex.Replace(normalized, " *\\n *", "\n");
        normalized = Regex.Replace(normalized, "\\n{3,}", "\n\n");
        return normalized.Trim();
    }

    private static IReadOnlyDictionary<string, string> ExtractMetadata(IDocument document)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var meta in document.QuerySelectorAll("meta"))
        {
            var name = meta.GetAttribute("name") ?? meta.GetAttribute("property") ?? meta.GetAttribute("itemprop");
            var content = NormalizeInlineText(meta.GetAttribute("content"));
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(content))
                metadata[name] = content;
        }

        return metadata;
    }

    private static string? GetMetadata(IReadOnlyDictionary<string, string> metadata, params string[] names)
    {
        foreach (var name in names)
        {
            if (metadata.TryGetValue(name, out var value))
                return value;
        }

        return null;
    }

    private static Uri? ResolveUrl(Uri sourceUrl, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(sourceUrl, value, out var resolved))
            return null;

        return resolved.Scheme is "http" or "https" ? resolved : null;
    }

    private static string FormatHtmlResponse(WebPageExtractionResult extraction, string content)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(extraction.Title))
            builder.Append("# ").AppendLine(extraction.Title);
        if (!string.IsNullOrWhiteSpace(extraction.Description))
            builder.Append("> ").AppendLine(extraction.Description);

        builder.Append("Source: ").AppendLine(extraction.CanonicalUrl.AbsoluteUri);
        if (!string.IsNullOrWhiteSpace(extraction.PublishedAt))
            builder.Append("Published: ").AppendLine(extraction.PublishedAt);
        if (!string.IsNullOrWhiteSpace(extraction.Author))
            builder.Append("Author: ").AppendLine(extraction.Author);

        return builder.Append("\n---\n\n").Append(content).ToString();
    }

    private static string FormatNonHtmlResponse(Uri url, string contentType, string content) =>
        $"**URL:** {url}\n**Content Type:** {contentType}\n\n---\n\n{content}";

    private static bool IsHtmlContentType(string mediaType) =>
        mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase);

    private static bool IsTextualContentType(string mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
            return false;
        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return true;
        if (new[] { "+json", "+xml", "+yaml", "+toml", "+javascript" }
            .Any(suffix => mediaType.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            return true;
        if (!mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase))
            return false;

        var subtype = mediaType["application/".Length..];
        return new[] { "json", "xml", "xhtml+xml", "javascript", "x-javascript", "ecmascript",
            "typescript", "yaml", "toml", "csv", "sql", "graphql" }
            .Contains(subtype, StringComparer.OrdinalIgnoreCase);
    }

    private static string? NormalizeInlineText(string? text) => string.IsNullOrWhiteSpace(text)
        ? null
        : Regex.Replace(text, "\\s+", " ").Trim();
}

/// <summary>
/// Parsed page data retained between extraction and Agent-facing rendering.
/// </summary>
internal sealed record WebPageExtractionResult(
    Uri SourceUrl,
    Uri CanonicalUrl,
    string? Title,
    string? Description,
    string? Author,
    string? PublishedAt,
    IReadOnlyDictionary<string, string> Metadata,
    string OriginalHtml,
    string Text,
    string Markdown);
