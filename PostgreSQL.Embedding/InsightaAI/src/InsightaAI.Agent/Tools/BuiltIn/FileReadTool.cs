using System.Text.Json;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// 文件读取工具
/// 支持全文读取、按行读取（offset/limit）、读取头部/尾部
/// </summary>
public class FileReadTool : IToolExecutor
{
    private readonly IFileSystem _fileSystem;
    private readonly FileReadState _readState;

    public string Name => "read_file";

    public ToolDefinition Definition { get; }

    public FileReadTool(IFileSystem fileSystem, FileReadState readState)
    {
        _fileSystem = fileSystem;
        _readState = readState;

        Definition = new ToolDefinition
        {
            Name = Name,
            Description = "读取文本文件内容。支持读取全部内容或指定范围的行。适用于查看代码、日志、配置文件等。",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    file_path = new
                    {
                        type = "string",
                        description = "要读取的文件路径"
                    },
                    offset = new
                    {
                        type = "integer",
                        description = "起始行号（从 0 开始）。用于跳过文件开头的行。"
                    },
                    limit = new
                    {
                        type = "integer",
                        description = "要读取的行数。不指定则读取从 offset 到文件末尾的所有内容。"
                    }
                },
                required = new[] { "file_path" }
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
            var filePath = GetStringValue(args, "file_path");
            if (string.IsNullOrEmpty(filePath))
            {
                return ToolResult.FromError(
                    "Missing required parameter: file_path\n" +
                    "Required: {\"file_path\": \"string\"}\n" +
                    "Optional: {\"offset\": number, \"limit\": number}");
            }

            var offset = GetIntValue(args, "offset");
            var limit = GetIntValue(args, "limit");

            // 检查文件是否存在
            if (!await _fileSystem.ExistsAsync(filePath, context.CancellationToken))
            {
                return ToolResult.FromError($"File not found: {filePath}");
            }

            // 获取文件修改时间
            var fileInfo = new FileInfo(Path.GetFullPath(filePath));
            var lastModified = fileInfo.LastWriteTimeUtc;

            // 读取文件
            if (offset.HasValue || limit.HasValue)
            {
                // 按行读取
                var result = await _fileSystem.ReadFileLinesAsync(
                    filePath,
                    offset,
                    limit,
                    context.CancellationToken);

                // 读取完整内容用于状态跟踪（edit_file 需要）
                var fullContent = await _fileSystem.ReadFileAsync(filePath, context.CancellationToken);
                _readState.RecordRead(filePath, fullContent, lastModified);

                var content = AddLineNumbers(result.Content, result.StartLine);
                return ToolResult.FromText(
                    $"File: {result.Path}\n" +
                    $"Lines: {result.StartLine}-{result.StartLine + result.LineCount} of {result.TotalLines}\n" +
                    $"---\n{content}");
            }
            else
            {
                // 全文读取
                var content = await _fileSystem.ReadFileAsync(filePath, context.CancellationToken);

                // 记录读取状态
                _readState.RecordRead(filePath, content, lastModified);

                // 检查文件大小限制（默认 100KB）
                if (content.Length > 100_000)
                {
                    return ToolResult.FromText(
                        $"File is too large ({content.Length} characters). " +
                        "Please use offset and limit parameters to read specific portions of the file.\n" +
                        $"Total lines: {content.Split('\n').Length}");
                }

                var lines = content.Split('\n');
                var numberedContent = AddLineNumbers(content, 0);
                return ToolResult.FromText(
                    $"File: {filePath}\n" +
                    $"Lines: 0-{lines.Length} of {lines.Length}\n" +
                    $"---\n{numberedContent}");
            }
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Failed to read file: {ex.Message}");
        }
    }

    private static string AddLineNumbers(string content, int startLine)
    {
        var lines = content.Split('\n');
        var sb = new System.Text.StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            sb.AppendLine($"{startLine + i,6}\t{lines[i]}");
        }

        return sb.ToString();
    }

    private static string? GetStringValue(IDictionary<string, object> args, string key)
    {
        if (args.TryGetValue(key, out var value))
        {
            return value?.ToString();
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
