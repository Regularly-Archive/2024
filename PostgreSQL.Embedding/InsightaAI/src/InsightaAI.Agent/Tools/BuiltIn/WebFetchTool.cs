using System.Text.RegularExpressions;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// Web 内容获取工具（Attribute 模式）
/// 获取 URL 内容并转换为 Markdown
/// </summary>
public static class WebFetchTool
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    /// 获取 URL 内容
    /// </summary>
    [Tool("web_fetch", "获取网页内容。返回 Markdown 格式的文本内容。适用于读取文档、文章、API 文档等。")]
    public static async Task<ToolResult> FetchAsync(
        [ToolParameter("要获取的 URL")] string url,
        [ToolParameter("是否提取纯文本（去掉 HTML 标签）")] bool extract_text,
        ToolExecutionContext context)
    {
        try
        {
            // 验证 URL
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return ToolResult.FromError($"Invalid URL: {url}");
            }

            // 只允许 HTTP/HTTPS
            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                return ToolResult.FromError("Only HTTP and HTTPS URLs are supported.");
            }

            // 设置 User-Agent
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; InsightaAI/1.0)");

            // 发送请求
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                context.CancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return ToolResult.FromError(
                    $"Failed to fetch URL: {response.StatusCode} {response.ReasonPhrase}");
            }

            // 检查内容类型
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var isHtml = contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase);
            var isText = contentType.Contains("text/", StringComparison.OrdinalIgnoreCase);

            if (!isText && !isHtml)
            {
                return ToolResult.FromError(
                    $"Unsupported content type: {contentType}. Only text and HTML are supported.");
            }

            // 读取内容
            var content = await response.Content.ReadAsStringAsync(context.CancellationToken);

            // 处理内容
            string result;
            if (isHtml && extract_text)
            {
                result = HtmlToMarkdown(content);
            }
            else if (isHtml)
            {
                result = ExtractTextFromHtml(content);
            }
            else
            {
                result = content;
            }

            // 检查内容长度
            if (result.Length > 50_000)
            {
                result = result[..50_000] + "\n\n... (content truncated)";
            }

            return ToolResult.FromText(
                $"**URL:** {url}\n" +
                $"**Content Type:** {contentType}\n\n" +
                $"---\n\n{result}");
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
    /// 从 HTML 提取纯文本
    /// </summary>
    private static string ExtractTextFromHtml(string html)
    {
        // 简单的 HTML 标签移除
        var text = Regex.Replace(html, "<script[^>]*>[\\s\\S]*?</script>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<style[^>]*>[\\s\\S]*?</style>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = Regex.Replace(text, "\\s+", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
    }

    /// <summary>
    /// 将 HTML 转换为简单的 Markdown
    /// </summary>
    private static string HtmlToMarkdown(string html)
    {
        var result = html;

        // 移除 script 和 style
        result = Regex.Replace(result, "<script[^>]*>[\\s\\S]*?</script>", "", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<style[^>]*>[\\s\\S]*?</style>", "", RegexOptions.IgnoreCase);

        // 标题
        result = Regex.Replace(result, "<h1[^>]*>(.*?)</h1>", "# $1\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<h2[^>]*>(.*?)</h2>", "## $1\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<h3[^>]*>(.*?)</h3>", "### $1\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<h4[^>]*>(.*?)</h4>", "#### $1\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<h5[^>]*>(.*?)</h5>", "##### $1\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<h6[^>]*>(.*?)</h6>", "###### $1\n", RegexOptions.IgnoreCase);

        // 粗体和斜体
        result = Regex.Replace(result, "<(?:b|strong)[^>]*>(.*?)</(?:b|strong)>", "**$1**", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<(?:i|em)[^>]*>(.*?)</(?:i|em)>", "*$1*", RegexOptions.IgnoreCase);

        // 链接
        result = Regex.Replace(result, "<a[^>]*href=\"([^\"]+)\"[^>]*>(.*?)</a>", "[$2]($1)", RegexOptions.IgnoreCase);

        // 图片
        result = Regex.Replace(result, "<img[^>]*src=\"([^\"]+)\"[^>]*alt=\"([^\"]*)\"[^>]*/?>", "![$2]($1)", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<img[^>]*src=\"([^\"]+)\"[^>]*/?>", "![]($1)", RegexOptions.IgnoreCase);

        // 列表
        result = Regex.Replace(result, "<li[^>]*>(.*?)</li>", "- $1\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<(?:ul|ol)[^>]*>", "\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "</(?:ul|ol)>", "\n", RegexOptions.IgnoreCase);

        // 段落和换行
        result = Regex.Replace(result, "<p[^>]*>(.*?)</p>", "$1\n\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);

        // 代码块
        result = Regex.Replace(result, "<pre[^>]*><code[^>]*>(.*?)</code></pre>", "```\n$1\n```\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        result = Regex.Replace(result, "<code[^>]*>(.*?)</code>", "`$1`", RegexOptions.IgnoreCase);

        // 移除剩余的 HTML 标签
        result = Regex.Replace(result, "<[^>]+>", "");

        // 解码 HTML 实体
        result = System.Net.WebUtility.HtmlDecode(result);

        // 清理多余的空白
        result = Regex.Replace(result, "\n{3,}", "\n\n");
        result = result.Trim();

        return result;
    }
}
