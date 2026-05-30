using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// Web 搜索工具（Attribute 模式示例）
/// 使用 Tavily API 进行搜索
/// </summary>
public static class WebSearchTool
{
    private static readonly HttpClient _httpClient = new();

    /// <summary>
    /// 使用 Tavily API 搜索
    /// </summary>
    [Tool("web_search", "搜索互联网获取实时信息。适用于查找最新资讯、文档、教程等。")]
    public static async Task<ToolResult> SearchAsync(
        [ToolParameter("搜索关键词")] string query,
        [ToolParameter("搜索结果数量限制")] int max_results,
        ToolExecutionContext context)
    {
        try
        {
            // 从环境变量获取 API Key
            var apiKey = Environment.GetEnvironmentVariable("TAVILY_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                return ToolResult.FromError(
                    "TAVILY_API_KEY environment variable is not set. " +
                    "Please set it to use web search functionality.");
            }

            // 构建请求
            var request = new TavilySearchRequest
            {
                ApiKey = apiKey,
                Query = query,
                MaxResults = Math.Min(max_results, 10),
                SearchDepth = "basic",
                IncludeAnswer = true
            };

            // 发送请求
            var response = await _httpClient.PostAsJsonAsync(
                "https://api.tavily.com/search",
                request,
                context.CancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(context.CancellationToken);
                return ToolResult.FromError($"Search API error: {response.StatusCode} - {error}");
            }

            // 解析响应
            var result = await response.Content.ReadFromJsonAsync<TavilySearchResponse>(
                cancellationToken: context.CancellationToken);

            if (result == null)
            {
                return ToolResult.FromError("Failed to parse search results.");
            }

            // 格式化输出
            var sb = new System.Text.StringBuilder();

            if (!string.IsNullOrEmpty(result.Answer))
            {
                sb.AppendLine($"**Answer:** {result.Answer}");
                sb.AppendLine();
            }

            if (result.Results != null && result.Results.Count > 0)
            {
                sb.AppendLine($"**Search Results ({result.Results.Count}):**");
                sb.AppendLine();

                foreach (var item in result.Results)
                {
                    sb.AppendLine($"- **{item.Title}**");
                    sb.AppendLine($"  URL: {item.Url}");
                    if (!string.IsNullOrEmpty(item.Content))
                    {
                        sb.AppendLine($"  {TruncateContent(item.Content, 200)}");
                    }
                    sb.AppendLine();
                }
            }

            return ToolResult.FromText(sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Web search failed: {ex.Message}");
        }
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (content.Length <= maxLength) return content;
        return content[..maxLength] + "...";
    }

    // Tavily API 类型定义
    private class TavilySearchRequest
    {
        [JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = "";

        [JsonPropertyName("query")]
        public string Query { get; set; } = "";

        [JsonPropertyName("max_results")]
        public int MaxResults { get; set; } = 5;

        [JsonPropertyName("search_depth")]
        public string SearchDepth { get; set; } = "basic";

        [JsonPropertyName("include_answer")]
        public bool IncludeAnswer { get; set; } = true;
    }

    private class TavilySearchResponse
    {
        [JsonPropertyName("answer")]
        public string? Answer { get; set; }

        [JsonPropertyName("results")]
        public List<TavilySearchResult>? Results { get; set; }
    }

    private class TavilySearchResult
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
