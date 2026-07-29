using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Models;
using System.Text.Json;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// 文件编辑工具 - 支持字符串匹配的精确替换
/// 需要先 read_file 读取文件，然后才能编辑
/// </summary>
public class FileEditTool : ITool
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
            Description = "Precisely edit file content. Finds and replaces content by exact string matching." +
                         "Must read the file with read_file first, and confirm the file hasn't been modified since." +
                         "Suitable for modifying functions, updating config, fixing bugs, and other precise editing scenarios.",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    file_path = new
                    {
                        type = "string",
                        description = "The file path to edit"
                    },
                    old_string = new
                    {
                        type = "string",
                        description = "The original string to find and replace (must match exactly, including whitespace and indentation)"
                    },
                    new_string = new
                    {
                        type = "string",
                        description = "The replacement string"
                    },
                    replace_all = new
                    {
                        type = "boolean",
                        description = "Whether to replace all occurrences (default false, only replaces the first match)"
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
            var arguments = new ToolArgumentReader(Definition.Schema, args);
            var filePath = arguments.GetString("file_path");
            var oldString = arguments.GetString("old_string");
            var newString = arguments.GetString("new_string");
            var replaceAll = arguments.GetBoolean("replace_all");

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
            var normalizedContent = NormalizeLineEndings(readInfo.Content);
            var normalizedOldString = NormalizeLineEndings(oldString);
            var normalizedNewString = NormalizeLineEndings(newString);

            var originalEncoding = _fileSystem.DetectEncoding(filePath);
            var originalLineEndingStyle = DetectLineEndingStyle(readInfo.Content);
            var originalLineEnding = ResolveOutputLineEnding(originalLineEndingStyle);

            // 6. 执行替换
            if (replaceAll)
            {
                // 替换所有匹配
                var count = CountOccurrences(normalizedContent, normalizedOldString);
                if (count == 0)
                {
                    return ToolResult.FromError(
                        $"No matches found for old_string in '{filePath}'.\n" +
                        "Make sure the string matches exactly, including whitespace and indentation.");
                }

                var newContent = normalizedContent.Replace(normalizedOldString, normalizedNewString);
                newContent = ApplyLineEnding(newContent, originalLineEnding);

                // 写入文件
                await _fileSystem.WriteFileAsync(filePath, newContent, originalEncoding, context.CancellationToken);

                // 更新读取状态
                var newFileInfo = new FileInfo(Path.GetFullPath(filePath));
                _readState.RecordRead(filePath, newContent, newFileInfo.LastWriteTimeUtc);

                return ToolResult.FromText(
                    $"The file '{filePath}' has been updated successfully. " +
                    $"All {count} occurrences were replaced.");
            }
            else
            {
                // 替换第一个匹配
                var index = normalizedContent.IndexOf(normalizedOldString, StringComparison.Ordinal);
                if (index == -1)
                {
                    return ToolResult.FromError(
                        $"No match found for old_string in '{filePath}'.\n" +
                        "Make sure the string matches exactly, including whitespace and indentation.\n" +
                        "Tip: Use read_file to view the exact content of the file.");
                }

                // 检查是否有多个匹配
                var secondIndex = normalizedContent.IndexOf(normalizedOldString, index + normalizedOldString.Length, StringComparison.Ordinal);
                if (secondIndex != -1)
                {
                    return ToolResult.FromError(
                        $"Multiple matches found for old_string in '{filePath}'.\n" +
                        "Please provide more context to make the match unique, or use replace_all: true.");
                }

                // 执行替换
                var newContent = normalizedContent.Remove(index, normalizedOldString.Length).Insert(index, normalizedNewString);
                newContent = ApplyLineEnding(newContent, originalLineEnding);

                // 写入文件
                await _fileSystem.WriteFileAsync(filePath, newContent, originalEncoding, context.CancellationToken);

                // 更新读取状态
                var newFileInfo = new FileInfo(Path.GetFullPath(filePath));
                _readState.RecordRead(filePath, newContent, newFileInfo.LastWriteTimeUtc);

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

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static string ApplyLineEnding(string text, string lineEnding)
    {
        return text.Replace("\n", lineEnding);
    }

    private enum LineEndingStyle { CRLF, LF, Mixed, Unknown }

    private static LineEndingStyle DetectLineEndingStyle(string text)
    {
        bool hasCrlf = text.Contains("\r\n");
        bool hasLf = text.Replace("\r\n", "").Contains('\n');

        if (!hasCrlf && !hasLf)
        {
            hasLf = text.Contains('\n');
            if (!hasLf && text.Contains('\r'))
                return LineEndingStyle.Mixed;
        }

        if (hasCrlf && hasLf) return LineEndingStyle.Mixed;
        if (hasCrlf) return LineEndingStyle.CRLF;
        if (hasLf) return LineEndingStyle.LF;
        return LineEndingStyle.Unknown;
    }

    private static string ResolveOutputLineEnding(LineEndingStyle original)
    {
        return original switch
        {
            LineEndingStyle.CRLF => "\r\n",
            LineEndingStyle.LF => "\n",
            LineEndingStyle.Mixed => Environment.NewLine,
            _ => Environment.NewLine
        };
    }

}
