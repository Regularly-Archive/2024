using System.Text.Json;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// 文件名模式搜索工具
/// 支持通配符模式搜索文件
/// </summary>
public class GlobTool : IToolExecutor
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
            Description = "按文件名模式搜索文件。支持通配符 * 和 ?。适用于查找特定类型的文件或匹配特定命名规则的文件。",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    pattern = new
                    {
                        type = "string",
                        description = "文件搜索模式，如 '*.cs'、'*.txt'、'test_*.*'"
                    },
                    path = new
                    {
                        type = "string",
                        description = "搜索的目录路径。默认当前目录。"
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
                return ToolResult.FromError("Missing required parameter: pattern");
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
