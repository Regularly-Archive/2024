using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// 文件内容搜索工具
/// 支持正则表达式、递归搜索、忽略大小写等选项
/// </summary>
public class GrepTool : ITool, IToolResultProjector
{
    private readonly IFileSystem _fileSystem;

    public string Name => "grep";

    public ToolDefinition Definition { get; }

    public GrepTool(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;

        Definition = new ToolDefinition
        {
            Name = Name,
            Description = "Search for keywords or regex patterns in files. Returns matching lines with line numbers. Suitable for finding functions, variables, error messages, etc. in code.",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    pattern = new
                    {
                        type = "string",
                        description = "The keyword or regex pattern to search for"
                    },
                    path = new
                    {
                        type = "string",
                        description = "The file or directory path to search"
                    },
                    recursive = new
                    {
                        type = "boolean",
                        description = "Whether to search subdirectories recursively. Default is true."
                    },
                    ignore_case = new
                    {
                        type = "boolean",
                        description = "Whether to ignore case. Default is false."
                    },
                    use_regex = new
                    {
                        type = "boolean",
                        description = "Whether to use regex for the pattern. Default is true."
                    },
                    files_only = new
                    {
                        type = "boolean",
                        description = "Whether to show only matching file names. Default is false."
                    },
                    exclude = new
                    {
                        type = "string",
                        description = "File or directory patterns to exclude, separated by commas. e.g. '*.log,node_modules'"
                    },
                    max_results = new
                    {
                        type = "integer",
                        description = "Maximum number of results to return. Default is 100."
                    }
                },
                required = new[] { "pattern" }
            })
        };
    }

    public async Task<ToolResult> ExecuteAsync(
        IDictionary<string, object> args,
        ToolExecutionContext context)
    {
        try
        {
            // 获取参数
            var pattern = GetStringValue(args, "pattern");
            if (string.IsNullOrEmpty(pattern))
            {
                return ToolResult.FromError(
                    "Missing required parameter: pattern\n" +
                    "Required: {\"pattern\": \"string\", \"path\": \"string\"}\n" +
                    "Optional: {\"recursive\": boolean, \"ignore_case\": boolean, \"use_regex\": boolean, \"files_only\": boolean, \"exclude\": \"string\", \"max_results\": number}");
            }

            var path = GetStringValue(args, "path");
            if (string.IsNullOrEmpty(path))
            {
                path = Environment.CurrentDirectory;
            }

            var recursive = GetBoolValue(args, "recursive") ?? true;
            var ignoreCase = GetBoolValue(args, "ignore_case") ?? false;
            var useRegex = GetBoolValue(args, "use_regex") ?? true;
            var filesOnly = GetBoolValue(args, "files_only") ?? false;
            var exclude = GetStringValue(args, "exclude");
            var maxResults = GetIntValue(args, "max_results") ?? 100;

            // 解析排除模式
            var excludePatterns = string.IsNullOrEmpty(exclude)
                ? Array.Empty<string>()
                : exclude.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .ToArray();

            // 构建选项
            var options = new GrepOptions
            {
                Recursive = recursive,
                IgnoreCase = ignoreCase,
                UseRegex = useRegex,
                FilesOnly = filesOnly,
                ExcludePatterns = excludePatterns,
                MaxResults = maxResults
            };

            // 检查路径是否存在
            if (!await _fileSystem.ExistsAsync(path, context.CancellationToken))
            {
                return ToolResult.FromError($"Path not found: {path}");
            }

            // 执行搜索
            var result = await _fileSystem.GrepAsync(pattern, path, options, context.CancellationToken);

            // 格式化输出
            if (result.Matches.Count == 0)
            {
                return ToolResult.FromText("No matches found.");
            }

            if (filesOnly)
            {
                // Show files with match counts, sorted by count descending
                var fileGroups = result.Matches
                    .GroupBy(m => m.FilePath)
                    .Select(g => new { FilePath = g.Key, Count = g.Count() })
                    .OrderByDescending(g => g.Count)
                    .ToArray();

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Found {result.TotalMatches} matches in {fileGroups.Length} files:");
                sb.AppendLine();
                foreach (var fg in fileGroups)
                {
                    sb.AppendLine($"  {fg.Count,4} matches in {fg.FilePath}");
                }

                if (result.Truncated)
                {
                    sb.AppendLine("\n(Results truncated. Consider using a more specific path or pattern.)");
                }

                return ToolResult.FromText(sb.ToString());
            }
            else
            {
                // 显示匹配的行
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Found {result.TotalMatches} matches in {result.FileCount} files:");

                foreach (var match in result.Matches)
                {
                    sb.AppendLine($"{match.FilePath}:{match.LineNumber}: {match.LineContent}");
                }

                if (result.Truncated)
                {
                    sb.AppendLine("\n(Results truncated. Use max_results parameter to see more.)");
                }

                return ToolResult.FromText(sb.ToString());
            }
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Grep failed: {ex.Message}");
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
        var fileMatches = text.Split('\n')
            .Select(line => line.IndexOf(':') is var index && index > 0 ? line[..index] : null)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .GroupBy(path => path!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count());
        var summary = new System.Text.StringBuilder();
        summary.AppendLine($"[Large grep result summarized; original length: {context.OriginalLength} chars]");
        foreach (var group in fileMatches)
            summary.AppendLine($"  {group.Count(),4} matches in {group.Key}");
        if (context.Artifact != null)
            summary.AppendLine($"Full output saved as artifact {context.Artifact.Id}: {context.Artifact.Path}");
        return new ToolResultProjection
        {
            Content = [new TextBlock { Text = summary.ToString() }],
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
        {
            return value?.ToString();
        }
        return null;
    }

    private static bool? GetBoolValue(IDictionary<string, object> args, string key)
    {
        if (args.TryGetValue(key, out var value) && value != null)
        {
            if (value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.True) return true;
                if (jsonElement.ValueKind == JsonValueKind.False) return false;
            }
            else if (bool.TryParse(value.ToString(), out var parsedValue))
            {
                return parsedValue;
            }
        }
        return null;
    }

    private static int? GetIntValue(IDictionary<string, object> args, string key)
    {
        if (args.TryGetValue(key, out var value) && value != null)
        {
            if (value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Number && jsonElement.TryGetInt32(out var intValue))
                {
                    return intValue;
                }
            }
            else if (int.TryParse(value.ToString(), out var parsedValue))
            {
                return parsedValue;
            }
        }
        return null;
    }
}
