using System.Text.Json;
using System.Text.RegularExpressions;
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
                    excludes = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "File or directory glob patterns to exclude, e.g. ['*.log', 'node_modules/**']"
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
            if (args.ContainsKey("exclude"))
                return ToolResult.FromError("Parameter 'exclude' is not supported. Use 'excludes' as an array of strings.");

            var arguments = new ToolArgumentReader(Definition.Schema, args);
            var pattern = arguments.GetString("pattern");
            var path = arguments.GetString("path", Environment.CurrentDirectory);
            var recursive = arguments.GetBoolean("recursive", true);
            var ignoreCase = arguments.GetBoolean("ignore_case");
            var useRegex = arguments.GetBoolean("use_regex", true);
            var filesOnly = arguments.GetBoolean("files_only");
            var maxResults = arguments.GetInt32("max_results", 100);
            if (maxResults <= 0)
                return ToolResult.FromError("Parameter max_results must be greater than zero.");

            var excludePatterns = arguments.GetStringArray("excludes");

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
                sb.AppendLine(result.Truncated
                    ? $"Found at least {result.TotalMatches} matches in {fileGroups.Length} files (partial results):"
                    : $"Found {result.TotalMatches} matches in {fileGroups.Length} files:");
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
                sb.AppendLine(result.Truncated
                    ? $"Found at least {result.TotalMatches} matches in {result.FileCount} files (partial results):"
                    : $"Found {result.TotalMatches} matches in {result.FileCount} files:");

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
        catch (OperationCanceledException)
        {
            throw;
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
            .Select(TryGetFilePathFromResultLine)
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

    private static string? TryGetFilePathFromResultLine(string line)
    {
        var match = Regex.Match(line, @"^(?<path>.+):\d+:\s");
        if (match.Success)
            return match.Groups["path"].Value;

        match = Regex.Match(line, @"^\s*\d+ matches in (?<path>.+)$");
        return match.Success ? match.Groups["path"].Value : null;
    }

}
