using System.Text;
using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// 文件写入工具
/// 支持创建/覆盖写入和追加写入
/// </summary>
public class FileWriteTool : ITool
{
    private readonly IFileSystem _fileSystem;
    private readonly IPathValidator _pathValidator;

    public string Name => "write_file";

    public ToolDefinition Definition { get; }

    public FileWriteTool(IFileSystem fileSystem, IPathValidator pathValidator)
    {
        _fileSystem = fileSystem;
        _pathValidator = pathValidator;

        Definition = new ToolDefinition
        {
            Name = Name,
            Description = "Create or write to a file. Supports overwrite and append modes.",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    file_path = new
                    {
                        type = "string",
                        description = "The file path to write to"
                    },
                    content = new
                    {
                        type = "string",
                        description = "The content to write"
                    },
                    append = new
                    {
                        type = "boolean",
                        description = "Whether to append. true appends to the end of the file, false overwrites. Default is false."
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
            var arguments = new ToolArgumentReader(Definition.Schema, args);
            var filePath = arguments.GetString("file_path");
            var content = arguments.GetString("content");
            var append = arguments.GetBoolean("append");

            // 路径安全验证
            var validation = _pathValidator.Validate(filePath);
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
                await _fileSystem.WriteFileAsync(filePath, content, Encoding.UTF8, context.CancellationToken);
                return ToolResult.FromText($"File written successfully: {filePath}");
            }
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Failed to write file: {ex.Message}");
        }
    }

}
