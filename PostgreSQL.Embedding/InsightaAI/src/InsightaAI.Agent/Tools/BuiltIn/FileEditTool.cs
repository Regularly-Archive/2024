using System.Text.Json;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// 文件编辑工具 - 支持字符串匹配的精确替换
/// 需要先 read_file 读取文件，然后才能编辑
/// </summary>
public class FileEditTool : IToolExecutor
{
    private readonly IFileSystem _fileSystem;
    private readonly FileReadState _readState;

    public string Name => "edit_file";

    public ToolDefinition Definition { get; }

    public FileEditTool(IFileSystem fileSystem, FileReadState readState)
    {
        _fileSystem = fileSystem;
        _readState = readState;

        Definition = new ToolDefinition
        {
            Name = Name,
            Description = "精确编辑文件内容。通过字符串匹配找到要修改的部分并替换。" +
                         "必须先用 read_file 读取文件，确认文件未被修改后才能编辑。" +
                         "适用于修改函数、更新配置、修复 bug 等精确编辑场景。",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    file_path = new
                    {
                        type = "string",
                        description = "要编辑的文件路径"
                    },
                    old_string = new
                    {
                        type = "string",
                        description = "要查找并替换的原始字符串（必须完全匹配，包括空格和缩进）"
                    },
                    new_string = new
                    {
                        type = "string",
                        description = "替换后的新字符串"
                    },
                    replace_all = new
                    {
                        type = "boolean",
                        description = "是否替换所有匹配项（默认 false，只替换第一个匹配）"
                    }
                },
                required = new[] { "file_path", "old_string", "new_string" }
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
                    "Required: {\"file_path\": \"string\", \"old_string\": \"string\", \"new_string\": \"string\"}\n" +
                    "Optional: {\"replace_all\": boolean}");
            }

            var oldString = GetStringValue(args, "old_string");
            if (string.IsNullOrEmpty(oldString))
            {
                return ToolResult.FromError(
                    "Missing required parameter: old_string\n" +
                    "Required: {\"file_path\": \"string\", \"old_string\": \"string\", \"new_string\": \"string\"}\n" +
                    "Optional: {\"replace_all\": boolean}");
            }

            var newString = GetStringValue(args, "new_string");
            if (newString == null)
            {
                return ToolResult.FromError(
                    "Missing required parameter: new_string\n" +
                    "Required: {\"file_path\": \"string\", \"old_string\": \"string\", \"new_string\": \"string\"}\n" +
                    "Optional: {\"replace_all\": boolean}");
            }

            var replaceAll = GetBoolValue(args, "replace_all");

            // 路径安全验证
            var validation = PathValidator.Validate(filePath);
            if (!validation.IsSafe)
            {
                return ToolResult.FromError(validation.ErrorMessage!);
            }
            filePath = validation.ResolvedPath!;

            // 1. 检查文件是否存在
            if (!await _fileSystem.ExistsAsync(filePath, context.CancellationToken))
            {
                return ToolResult.FromError($"File not found: {filePath}");
            }

            // 2. 获取文件当前修改时间
            var fileInfo = new FileInfo(Path.GetFullPath(filePath));
            var currentLastModified = fileInfo.LastWriteTimeUtc;

            // 3. 检查文件是否已读取
            var readInfo = _readState.GetReadInfo(filePath);
            if (readInfo == null)
            {
                return ToolResult.FromError(
                    $"File has not been read yet. Please use read_file to read '{filePath}' first.");
            }

            // 4. 检查文件是否被修改过
            if (_readState.IsFileModifiedSinceRead(filePath, currentLastModified))
            {
                return ToolResult.FromError(
                    $"File '{filePath}' has been modified since it was last read. " +
                    "Please use read_file to read it again before editing.");
            }

            // 5. 使用缓存的内容进行编辑（保证原子性）
            var content = readInfo.Content;

            // 6. 执行替换
            if (replaceAll == true)
            {
                // 替换所有匹配
                var count = CountOccurrences(content, oldString);
                if (count == 0)
                {
                    return ToolResult.FromError(
                        $"No matches found for old_string in '{filePath}'.\n" +
                        "Make sure the string matches exactly, including whitespace and indentation.");
                }

                content = content.Replace(oldString, newString);

                // 写入文件
                await _fileSystem.WriteFileAsync(filePath, content, context.CancellationToken);

                // 更新读取状态
                var newFileInfo = new FileInfo(Path.GetFullPath(filePath));
                _readState.RecordRead(filePath, content, newFileInfo.LastWriteTimeUtc);

                return ToolResult.FromText(
                    $"The file '{filePath}' has been updated successfully. " +
                    $"All {count} occurrences were replaced.");
            }
            else
            {
                // 替换第一个匹配
                var index = content.IndexOf(oldString, StringComparison.Ordinal);
                if (index == -1)
                {
                    return ToolResult.FromError(
                        $"No match found for old_string in '{filePath}'.\n" +
                        "Make sure the string matches exactly, including whitespace and indentation.\n" +
                        "Tip: Use read_file to view the exact content of the file.");
                }

                // 检查是否有多个匹配
                var secondIndex = content.IndexOf(oldString, index + oldString.Length, StringComparison.Ordinal);
                if (secondIndex != -1)
                {
                    return ToolResult.FromError(
                        $"Multiple matches found for old_string in '{filePath}'.\n" +
                        "Please provide more context to make the match unique, or use replace_all: true.");
                }

                // 执行替换
                content = content.Remove(index, oldString.Length).Insert(index, newString);

                // 写入文件
                await _fileSystem.WriteFileAsync(filePath, content, context.CancellationToken);

                // 更新读取状态
                var newFileInfo = new FileInfo(Path.GetFullPath(filePath));
                _readState.RecordRead(filePath, content, newFileInfo.LastWriteTimeUtc);

                return ToolResult.FromText(
                    $"The file '{filePath}' has been updated successfully.");
            }
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Failed to edit file: {ex.Message}");
        }
    }

    /// <summary>
    /// 统计字符串出现次数
    /// </summary>
    private static int CountOccurrences(string source, string substring)
    {
        if (string.IsNullOrEmpty(substring)) return 0;

        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }
        return count;
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
