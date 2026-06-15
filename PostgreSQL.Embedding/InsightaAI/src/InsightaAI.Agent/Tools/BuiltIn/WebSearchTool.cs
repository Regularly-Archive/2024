using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using InsightaAI.Agent.Abstractions;
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
        [ToolParameter("搜索结果数量限制（默认 5）")] int max_results,
        ToolExecutionContext context,
        [ToolParameter("是否包含 AI 生成的答案（默认 true）")] bool include_answer = true,
        [ToolParameter("搜索深度：basic 或 advanced（默认 basic）")] string search_depth = "basic",
        [ToolParameter("只搜索这些域名（逗号分隔）")] string? include_domains = null,
        [ToolParameter("排除这些域名（逗号分隔）")] string? exclude_domains = null,
        [ToolParameter("搜索主题：general 或 news（默认 general）")] string topic = "general",
        [ToolParameter("新闻搜索的时间范围（天数，仅 topic=news 时有效）")] int days = 3)
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

            // 构建请求体
            var request = new TavilySearchRequest
            {
                Query = query,
                MaxResults = Math.Min(max_results, 20),
                SearchDepth = search_depth,
                IncludeAnswer = include_answer,
                Topic = topic,
                Days = days
            };

            // 处理域名过滤
            if (!string.IsNullOrEmpty(include_domains))
            {
                request.IncludeDomains = include_domains
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }

            if (!string.IsNullOrEmpty(exclude_domains))
            {
                request.ExcludeDomains = exclude_domains
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }

            // 使用 Bearer token 认证
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search")
            {
                Content = JsonContent.Create(request, options: new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                })
            };
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            // 发送请求
            var response = await _httpClient.SendAsync(requestMessage, context.CancellationToken);

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

            // 直接返回解析后的结果
            return ToolResult.From(result);
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Web search failed: {ex.Message}");
        }
    }

    private class TavilySearchRequest
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = "";

        [JsonPropertyName("max_results")]
        public int MaxResults { get; set; } = 5;

        [JsonPropertyName("search_depth")]
        public string SearchDepth { get; set; } = "basic";

        [JsonPropertyName("include_answer")]
        public bool IncludeAnswer { get; set; } = true;

        [JsonPropertyName("topic")]
        public string Topic { get; set; } = "general";

        [JsonPropertyName("days")]
        public int Days { get; set; } = 3;

        [JsonPropertyName("include_domains")]
        public List<string>? IncludeDomains { get; set; }

        [JsonPropertyName("exclude_domains")]
        public List<string>? ExcludeDomains { get; set; }
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
