using System.Text.Json;
using System.Text.RegularExpressions;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// Web 内容获取工具（IToolExecutor 模式，支持 Intercept）
/// 获取 URL 内容并转换为 Markdown
/// </summary>
public class WebFetchTool : ITool
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public string Name => "web_fetch";

    public ToolDefinition Definition => new()
    {
        Name = Name,
        Description = "获取网页内容。返回 Markdown 格式的文本内容。适用于读取文档、文章、API 文档等。",
        Schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                url = new
                {
                    type = "string",
                    description = "要获取的 URL"
                },
                extract_text = new
                {
                    type = "boolean",
                    description = "是否提取纯文本（去掉 HTML 标签）"
                }
            },
            required = new[] { "url", "extract_text" }
        })
    };

    public async Task<ToolResult> ExecuteAsync(
        IDictionary<string, object> args,
        ToolExecutionContext context)
    {
        var url = GetStringValue(args, "url") ?? "";
        var extractText = args.TryGetValue("extract_text", out var v) && v is bool b && b;

        try
        {
            // 验证 URL
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return ToolResult.FromError($"Invalid URL: {url}");

            if (uri.Scheme != "http" && uri.Scheme != "https")
                return ToolResult.FromError("Only HTTP and HTTPS URLs are supported.");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; InsightaAI/1.0)");

            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, context.CancellationToken);

            if (!response.IsSuccessStatusCode)
                return ToolResult.FromError($"Failed to fetch URL: {response.StatusCode} {response.ReasonPhrase}");

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var isHtml = contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase);
            var isText = contentType.Contains("text/", StringComparison.OrdinalIgnoreCase);

            if (!isText && !isHtml)
                return ToolResult.FromError($"Unsupported content type: {contentType}. Only text and HTML are supported.");

            var content = await response.Content.ReadAsStringAsync(context.CancellationToken);

            string result;
            if (isHtml && extractText)
                result = HtmlToMarkdown(content);
            else if (isHtml)
                result = ExtractTextFromHtml(content);
            else
                result = content;

            return ToolResult.FromText(
                $"**URL:** {url}\n**Content Type:** {contentType}\n\n---\n\n{result}");
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

    /// <summary>
    /// 拦截大网页内容：持久化到磁盘，上下文只保留预览
    /// </summary>
    public InterceptionResult Intercept(ToolResult result, TruncationContext context)
    {
        var text = result.Content.OfType<TextBlock>().FirstOrDefault()?.Text;
        if (text == null || context.OriginalLength <= 30_000)
            return InterceptionResult.NotIntercepted(result);

        // 持久化到磁盘
        Directory.CreateDirectory(context.ToolResultDirectory);
        var path = Path.Combine(context.ToolResultDirectory,
            $"WebFetch_{DateTime.Now:yyyyMMdd_HHmmss}_{context.ToolCallId}.txt");
        File.WriteAllText(path, text);

        // 保留前 5000 字符作为预览
        var preview = text[..Math.Min(5000, text.Length)];

        return new InterceptionResult(
            ToolResult.FromText($"{preview}\n\n[完整内容已保存: {path}] (共 {text.Length} 字符)"),
            toolResultIntercepted: true,
            persistedPath: path,
            originalLength: context.OriginalLength
        );
    }

    private static string? GetStringValue(IDictionary<string, object> args, string key)
    {
        if (args.TryGetValue(key, out var value))
            return value?.ToString();
        return null;
    }

    private static string ExtractTextFromHtml(string html)
    {
        var text = Regex.Replace(html, "<script[^>]*>[\\s\\S]*?</script>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<style[^>]*>[\\s\\S]*?</style>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = Regex.Replace(text, "\\s+", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
    }

    private static string HtmlToMarkdown(string html)
    {
        var result = html;

        result = Regex.Replace(result, "<script[^>]*>[\\s\\S]*?</script>", "", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<style[^>]*>[\\s\\S]*?</style>", "", RegexOptions.IgnoreCase);

        result = Regex.Replace(result, "<h1[^>]*>(.*?)</h1>", "# $1\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<h2[^>]*>(.*?)</h2>", "## $1\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<h3[^>]*>(.*?)</h3>", "### $1\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<h4[^>]*>(.*?)</h4>", "#### $1\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<h5[^>]*>(.*?)</h5>", "##### $1\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<h6[^>]*>(.*?)</h6>", "###### $1\n", RegexOptions.IgnoreCase);

        result = Regex.Replace(result, "<(?:b|strong)[^>]*>(.*?)</(?:b|strong)>", "**$1**", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<(?:i|em)[^>]*>(.*?)</(?:i|em)>", "*$1*", RegexOptions.IgnoreCase);

        result = Regex.Replace(result, "<a[^>]*href=\"([^\"]+)\"[^>]*>(.*?)</a>", "[$2]($1)", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<img[^>]*src=\"([^\"]+)\"[^>]*alt=\"([^\"]*)\"[^>]*/?>", "![$2]($1)", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<img[^>]*src=\"([^\"]+)\"[^>]*/?>", "![]($1)", RegexOptions.IgnoreCase);

        result = Regex.Replace(result, "<li[^>]*>(.*?)</li>", "- $1\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<(?:ul|ol)[^>]*>", "\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "</(?:ul|ol)>", "\n", RegexOptions.IgnoreCase);

        result = Regex.Replace(result, "<p[^>]*>(.*?)</p>", "$1\n\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);

        result = Regex.Replace(result, "<pre[^>]*><code[^>]*>(.*?)</code></pre>", "```\n$1\n```\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        result = Regex.Replace(result, "<code[^>]*>(.*?)</code>", "`$1`", RegexOptions.IgnoreCase);

        result = Regex.Replace(result, "<[^>]+>", "");
        result = System.Net.WebUtility.HtmlDecode(result);
        result = Regex.Replace(result, "\n{3,}", "\n\n");

        return result.Trim();
    }
}
