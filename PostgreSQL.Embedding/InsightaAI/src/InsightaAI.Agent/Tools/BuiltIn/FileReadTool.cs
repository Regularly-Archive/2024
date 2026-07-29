using System.Text.Json;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// 文件读取工具
/// 支持全文读取、按行读取（offset/limit）、读取头部/尾部
/// </summary>
public class FileReadTool : ITool, IToolResultProjector
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
            var arguments = new ToolArgumentReader(Definition.Schema, args);
            var filePath = arguments.GetString("file_path");
            var offset = arguments.GetInt32("offset", 0);
            var limit = arguments.GetInt32("limit", 120);

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

    public ToolResultRetentionPolicy RetentionPolicy { get; } = new()
    {
        CanReplay = true,
        PreferPersistence = true,
        MinimumLevel = ToolResultRetentionLevel.Removed
    };

    public ToolResultProjection CreatePreview(ToolResult result, ToolResultProjectionContext context)
    {
        var text = result.Content.OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty;
        var preview = string.Join("\n", text.Split('\n').Take(200));
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

}
