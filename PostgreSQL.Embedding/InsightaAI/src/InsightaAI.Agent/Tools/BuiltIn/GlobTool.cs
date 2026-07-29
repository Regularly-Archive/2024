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
            if (args.ContainsKey("exclude"))
                return ToolResult.FromError("Parameter 'exclude' is not supported. Use 'excludes' as an array of strings.");

            var arguments = new ToolArgumentReader(Definition.Schema, args);
            var pattern = arguments.GetString("pattern");
            var path = arguments.GetString("path", ".");
            var includeIgnored = arguments.GetBoolean("include_ignored");
            var additionalExcludes = arguments.GetStringArray("excludes");
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

}
