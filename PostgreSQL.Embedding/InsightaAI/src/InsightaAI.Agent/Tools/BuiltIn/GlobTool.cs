using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// 文件名模式搜索工具
/// 支持通配符模式搜索文件
/// </summary>
public class GlobTool : ITool
{
    private readonly IFileSystem _fileSystem;

    public string Name => "glob";

    public ToolDefinition Definition { get; }

    public GlobTool(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;

        Definition = new ToolDefinition
        {
            Name = Name,
            Description = "Search for files by name pattern. Supports wildcards * and ?. Suitable for finding specific file types or files matching a naming pattern.",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    pattern = new
                    {
                        type = "string",
                        description = "File search pattern, e.g. '*.cs', '*.txt', 'test_*.*'"
                    },
                    path = new
                    {
                        type = "string",
                        description = "The directory path to search. Defaults to the current directory."
                    },
                    excludes = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "Additional glob patterns to exclude, e.g. ['generated/**', '*.min.js']. Common build directories (bin, obj, node_modules) are excluded by default."
                    },
                    include_ignored = new
                    {
                        type = "boolean",
                        description = "Whether to include files in default-excluded directories (bin, obj, node_modules). Default is false."
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
                    "Required: {\"pattern\": \"string\"}\n" +
                    "Optional: {\"path\": \"string\"}");
            }

            var path = GetStringValue(args, "path") ?? ".";
            var includeIgnored = GetBoolValue(args, "include_ignored") ?? false;
            var additionalExcludes = GetStringArrayValue(args, "excludes");
            var options = new GlobOptions
            {
                UseDefaultExcludes = !includeIgnored,
                ExcludePatterns = additionalExcludes
            };

            // 检查路径是否存在
            if (!await _fileSystem.ExistsAsync(path, context.CancellationToken))
            {
                return ToolResult.FromError($"Path not found: {path}");
            }

            // 执行搜索
            var files = await _fileSystem.GlobAsync(pattern, path, options, context.CancellationToken);

            // 格式化输出
            if (files.Length == 0)
            {
                var message = "No files found matching the pattern.";
                if (!includeIgnored)
                {
                    message += " Default-excluded directories: bin, obj, node_modules. " +
                        "Set include_ignored=true to search them.";
                }

                return ToolResult.FromText(message);
            }

            // 截断过长的结果
            const int maxResults = 100;
            var truncated = files.Length > maxResults;
            var displayFiles = truncated ? files.Take(maxResults).ToArray() : files;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Found {files.Length} files matching '{pattern}':");
            sb.AppendLine();

            foreach (var file in displayFiles)
            {
                // 显示相对路径
                var relativePath = Path.GetRelativePath(path, file);
                sb.AppendLine(relativePath);
            }

            if (truncated)
            {
                sb.AppendLine($"\n(Showing first {maxResults} results. Use a more specific pattern to narrow down.)");
            }

            return ToolResult.FromText(sb.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Glob search failed: {ex.Message}");
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
        if (!args.TryGetValue(key, out var value) || value == null)
            return null;

        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }

        return bool.TryParse(value.ToString(), out var parsedValue) ? parsedValue : null;
    }

    private static string[] GetStringArrayValue(IDictionary<string, object> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value == null)
            return Array.Empty<string>();

        if (value is JsonElement { ValueKind: JsonValueKind.Array } jsonArray)
        {
            if (jsonArray.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
                throw new ArgumentException("Parameter excludes must be an array of strings.");

            return jsonArray.EnumerateArray()
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToArray();
        }

        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            var values = enumerable.Cast<object?>().ToArray();
            if (values.Any(item => item is not string))
                throw new ArgumentException("Parameter excludes must be an array of strings.");

            return values.Cast<string>()
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
        }

        throw new ArgumentException("Parameter excludes must be an array of strings.");
    }
}
