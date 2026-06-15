using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// 文件写入工具
/// 支持创建/覆盖写入和追加写入
/// </summary>
public class FileWriteTool : IToolExecutor
{
    private readonly IFileSystem _fileSystem;

    public string Name => "write_file";

    public ToolDefinition Definition { get; }

    public FileWriteTool(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;

        Definition = new ToolDefinition
        {
            Name = Name,
            Description = "创建或写入文件。支持覆盖写入和追加写入模式。",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    file_path = new
                    {
                        type = "string",
                        description = "要写入的文件路径"
                    },
                    content = new
                    {
                        type = "string",
                        description = "要写入的内容"
                    },
                    append = new
                    {
                        type = "boolean",
                        description = "是否追加模式。true 表示追加到文件末尾，false 表示覆盖。默认 false。"
                    }
                },
                required = new[] { "file_path", "content" }
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
                    "Required parameters: {\"file_path\": \"string\", \"content\": \"string\"}\n" +
                    "Optional: {\"append\": boolean}");
            }

            var content = GetStringValue(args, "content");
            if (content == null)
            {
                return ToolResult.FromError(
                    "Missing required parameter: content\n" +
                    "Required parameters: {\"file_path\": \"string\", \"content\": \"string\"}\n" +
                    "Optional: {\"append\": boolean}");
            }

            var append = GetBoolValue(args, "append") ?? false;

            // 路径安全验证
            var validation = PathValidator.Validate(filePath);
            if (!validation.IsSafe)
            {
                return ToolResult.FromError(validation.ErrorMessage!);
            }
            filePath = validation.ResolvedPath!;

            // 写入文件
            if (append)
            {
                await _fileSystem.AppendFileAsync(filePath, content, context.CancellationToken);
                return ToolResult.FromText($"Content appended to file: {filePath}");
            }
            else
            {
                await _fileSystem.WriteFileAsync(filePath, content, context.CancellationToken);
                return ToolResult.FromText($"File written successfully: {filePath}");
            }
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Failed to write file: {ex.Message}");
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
}
