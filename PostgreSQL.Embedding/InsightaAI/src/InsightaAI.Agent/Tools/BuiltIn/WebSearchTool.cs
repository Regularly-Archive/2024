using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// Web 搜索工具（IToolExecutor 模式，支持 Intercept）
/// 使用 Tavily API 进行搜索
/// </summary>
public class WebSearchTool : ITool, IToolResultProjector
{
    private static readonly HttpClient _httpClient = new();

    public string Name => "web_search";

    public ToolDefinition Definition => new()
    {
        Name = Name,
        Description = "Search the internet for real-time information. Suitable for finding latest news, documentation, tutorials, etc.",
        Schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                query = new
                {
                    type = "string",
                    description = "Search query"
                },
                max_results = new
                {
                    type = "integer",
                    description = "Maximum number of search results (default 5)"
                },
                search_depth = new
                {
                    type = "string",
                    description = "Search depth: 'basic' or 'advanced' (default 'basic')"
                },
                topic = new
                {
                    type = "string",
                    description = "Search topic: 'general' or 'news' (default 'general')"
                }
            },
            required = new[] { "query" }
        })
    };

    public async Task<ToolResult> ExecuteAsync(
        IDictionary<string, object> args,
        ToolExecutionContext context)
    {
        var query = GetStringValue(args, "query") ?? "";
        var maxResults = GetIntValue(args, "max_results", 5);
        var searchDepth = GetStringValue(args, "search_depth") ?? "basic";
        var topic = GetStringValue(args, "topic") ?? "general";
        var includeAnswer = !args.ContainsKey("include_answer") || args["include_answer"] is not bool b || b;
        var days = GetIntValue(args, "days", 3);
        var includeDomains = GetStringValue(args, "include_domains");
        var excludeDomains = GetStringValue(args, "exclude_domains");

        try
        {
            var apiKey = Environment.GetEnvironmentVariable("TAVILY_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
                return ToolResult.FromError("TAVILY_API_KEY environment variable is not set.");

            var request = new TavilySearchRequest
            {
                Query = query,
                MaxResults = Math.Min(maxResults, 20),
                SearchDepth = searchDepth,
                IncludeAnswer = includeAnswer,
                Topic = topic,
                Days = days
            };

            if (!string.IsNullOrEmpty(includeDomains))
            {
                request.IncludeDomains = includeDomains
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }

            if (!string.IsNullOrEmpty(excludeDomains))
            {
                request.ExcludeDomains = excludeDomains
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search")
            {
                Content = JsonContent.Create(request, options: new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                })
            };
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _httpClient.SendAsync(requestMessage, context.CancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(context.CancellationToken);
                return ToolResult.FromError($"Search API error: {response.StatusCode} - {error}");
            }

            var result = await response.Content.ReadFromJsonAsync<TavilySearchResponse>(
                cancellationToken: context.CancellationToken);

            if (result == null)
                return ToolResult.FromError("Failed to parse search results.");

            return ToolResult.From(result);
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Web search failed: {ex.Message}");
        }
    }

    public ToolResultRetentionPolicy RetentionPolicy { get; } = new()
    {
        CanReplay = true,
        MinimumLevel = ToolResultRetentionLevel.Removed
    };

    public ToolResultProjection CreatePreview(ToolResult result, ToolResultProjectionContext context)
    {
        var text = result.Content.OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty;
        var preview = text[..Math.Min(10000, text.Length)];
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

    private static string? GetStringValue(IDictionary<string, object> args, string key)
    {
        if (args.TryGetValue(key, out var value))
            return value?.ToString();
        return null;
    }

    private static int GetIntValue(IDictionary<string, object> args, string key, int defaultValue)
    {
        if (args.TryGetValue(key, out var value) && value is int i)
            return i;
        if (value?.ToString() is string s && int.TryParse(s, out var parsed))
            return parsed;
        return defaultValue;
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
