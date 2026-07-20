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

            // 检查路径是否存在
            if (!await _fileSystem.ExistsAsync(path, context.CancellationToken))
            {
                return ToolResult.FromError($"Path not found: {path}");
            }

            // 执行搜索
            var files = await _fileSystem.GlobAsync(pattern, path, context.CancellationToken);

            // 格式化输出
            if (files.Length == 0)
            {
                return ToolResult.FromText("No files found matching the pattern.");
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
}
