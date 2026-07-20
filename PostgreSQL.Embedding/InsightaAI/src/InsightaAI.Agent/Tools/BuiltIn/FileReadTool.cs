using System.Text.Json;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// 文件读取工具
/// 支持全文读取、按行读取（offset/limit）、读取头部/尾部
/// </summary>
public class FileReadTool : ITool
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
            Description = "Read text file content. Supports reading full content or a range of lines. Suitable for viewing code, logs, configuration files, etc.",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    file_path = new
                    {
                        type = "string",
                        description = "The file path to read"
                    },
                    offset = new
                    {
                        type = "integer",
                        description = "Starting line number (0-based). Used to skip lines at the beginning of the file."
                    },
                    limit = new
                    {
                        type = "integer",
                        description = "Number of lines to read. Defaults to 120 if not specified."
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

            var offset = GetIntValue(args, "offset", 0);
            var limit = GetIntValue(args, "limit", 120);

            // 检查文件是否存在
            if (!await _fileSystem.ExistsAsync(filePath, context.CancellationToken))
            {
                return ToolResult.FromError($"File not found: {filePath}");
            }

            // 获取文件修改时间
            var fileInfo = new FileInfo(Path.GetFullPath(filePath));
            var lastModified = fileInfo.LastWriteTimeUtc;

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

    /// <summary>
    /// 拦截大文件读取结果：持久化到磁盘，上下文只保留预览
    /// </summary>
    public InterceptionResult Intercept(ToolResult result, TruncationContext context)
    {
        var text = result.Content.OfType<TextBlock>().FirstOrDefault()?.Text;
        if (text == null || context.OriginalLength <= 30_000)
            return InterceptionResult.NotIntercepted(result);

        // 持久化到磁盘
        Directory.CreateDirectory(context.ToolResultDirectory);
        var path = Path.Combine(context.ToolResultDirectory,
            $"FileRead_{DateTime.Now:yyyyMMdd_HHmmss}_{context.ToolCallId}.txt");
        
        using (var writer = new StreamWriter(path))
        {
            writer.Write(text);
        }

        // 保留 200 行预览
        var lines = text.Split('\n');
        var preview = string.Join("\n", lines.Take(200));
        var lineCount = context.OriginalLineCount.Value;

        return new InterceptionResult(
            ToolResult.FromText($"{preview}\n\n[完整内容已保存: {path}] (共 {lineCount} 行)"),
            toolResultIntercepted: true,
            persistedPath: path,
            originalLength: context.OriginalLength
        );
    }

    private static string? GetStringValue(IDictionary<string, object> args, string key, string? defaultValue = null)
    {
        if (args.TryGetValue(key, out var value))
        {
            return value?.ToString();
        }
        return defaultValue;
    }
    private static int? GetIntValue(IDictionary<string, object> args, string key, int? defaultValue = null)
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
        return defaultValue;
    }
}
