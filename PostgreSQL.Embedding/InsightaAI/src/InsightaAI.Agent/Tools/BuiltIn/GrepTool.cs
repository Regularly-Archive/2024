using System.Text.Json;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// 文件内容搜索工具
/// 支持正则表达式、递归搜索、忽略大小写等选项
/// </summary>
public class GrepTool : IToolExecutor
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
            Description = "在文件中搜索关键词或正则表达式。返回匹配的行及其行号。适用于查找代码中的函数、变量、错误信息等。",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    pattern = new
                    {
                        type = "string",
                        description = "要搜索的关键词或正则表达式"
                    },
                    path = new
                    {
                        type = "string",
                        description = "要搜索的文件或目录路径"
                    },
                    recursive = new
                    {
                        type = "boolean",
                        description = "是否递归搜索子目录。默认 true。"
                    },
                    ignore_case = new
                    {
                        type = "boolean",
                        description = "是否忽略大小写。默认 false。"
                    },
                    use_regex = new
                    {
                        type = "boolean",
                        description = "是否使用正则表达式。默认 true。"
                    },
                    files_only = new
                    {
                        type = "boolean",
                        description = "是否只显示匹配的文件名。默认 false。"
                    },
                    exclude = new
                    {
                        type = "string",
                        description = "要排除的文件或目录模式，多个用逗号分隔。如：'*.log,node_modules'"
                    },
                    max_results = new
                    {
                        type = "integer",
                        description = "最大返回结果数。默认 100。"
                    }
                },
                required = new[] { "pattern", "path" }
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
                return ToolResult.FromError("Missing required parameter: pattern");
            }

            var path = GetStringValue(args, "path");
            if (string.IsNullOrEmpty(path))
            {
                return ToolResult.FromError("Missing required parameter: path");
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
                // 只显示文件名
                var files = result.Matches.Select(m => m.FilePath).Distinct().ToArray();
                var fileOutput = string.Join("\n", files);
                if (result.Truncated)
                {
                    fileOutput += "\n\n(Results truncated. Consider using a more specific path or pattern.)";
                }
                return ToolResult.FromText(
                    $"Found {files.Length} files with matches:\n{fileOutput}");
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
