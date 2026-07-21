using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// Shell 命令执行工具
/// 通过 IShellExecutor 接口支持多种执行环境（本地、Docker、远程等）
/// </summary>
public class BashTool : ITool, IToolResultProjector
{
    private readonly IShellExecutor _shellExecutor;

    public string Name => "bash";

    public ToolDefinition Definition { get; }

    public BashTool(IShellExecutor shellExecutor)
    {
        _shellExecutor = shellExecutor;

        Definition = new ToolDefinition
        {
            Name = Name,
            Description = "Execute shell commands. Suitable for running system commands, scripts, building code, etc." +
                         "On Windows, uses PowerShell; on Linux/Mac, uses Bash." +
                         "On Windows, prefer PowerShell cmdlets over native commands (Chinese output may be garbled)." +
                         "Common mappings: ipconfig → Get-NetIPAddress, tasklist → Get-Process, dir → Get-ChildItem, " +
                         "findstr → Select-String, mkdir → New-Item -ItemType Directory, del → Remove-Item, " +
                         "copy → Copy-Item, move → Move-Item, type → Get-Content, echo → Write-Output",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    command = new
                    {
                        type = "string",
                        description = "The shell command to execute"
                    },
                    working_directory = new
                    {
                        type = "string",
                        description = "The working directory for command execution. Defaults to the current directory."
                    }
                },
                required = new[] { "command" }
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
            var command = GetStringValue(args, "command");
            if (string.IsNullOrEmpty(command))
            {
                return ToolResult.FromError(
                    "Missing required parameter: command\n" +
                    "Required: {\"command\": \"string\"}\n" +
                    "Optional: {\"working_directory\": \"string\"}");
            }

            var workingDirectory = GetStringValue(args, "working_directory");

            // 安全检查：禁止危险命令
            if (IsDangerousCommand(command))
            {
                return ToolResult.FromError(
                    "This command is blocked for safety reasons. " +
                    "Dangerous commands like 'rm -rf /', 'format', etc. are not allowed.");
            }

            // 执行命令
            var result = await _shellExecutor.ExecuteAsync(
                command,
                workingDirectory,
                context.CancellationToken);

            // 格式化输出
            var sb = new System.Text.StringBuilder();

            if (!string.IsNullOrEmpty(result.Stdout))
            {
                sb.AppendLine(result.Stdout);
            }

            if (!string.IsNullOrEmpty(result.Stderr))
            {
                sb.AppendLine($"[stderr] {result.Stderr}");
            }

            sb.AppendLine($"[exit_code] {result.ExitCode}");

            var output = sb.ToString().TrimEnd();

            // 检查输出长度
            if (output.Length > 10_000)
            {
                return ToolResult.FromText(
                    $"Command output is too long ({output.Length} characters). " +
                    "Please use 'head' or 'tail' to limit the output:\n" +
                    $"  {command} | head -n 100\n" +
                    $"  {command} | tail -n 100\n" +
                    $"  {command} > output.txt");
            }

            return result.Success
                ? ToolResult.FromText(output)
                : ToolResult.FromError(output);
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Failed to execute command: {ex.Message}");
        }
    }

    public ToolResultRetentionPolicy RetentionPolicy { get; } = new()
    {
        HasSideEffects = true,
        MinimumLevel = ToolResultRetentionLevel.Placeholder
    };

    public ToolResultProjection CreatePreview(ToolResult result, ToolResultProjectionContext context)
    {
        var text = result.Content.OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty;
        var lines = text.Split('\n');
        var preview = string.Join("\n", lines.Take(50));
        if (lines.Length > 100)
            preview += $"\n\n[... omitted {lines.Length - 100} lines ...]\n\n" + string.Join("\n", lines.TakeLast(50));
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

    private static bool IsDangerousCommand(string command)
    {
        var dangerousPatterns = new[]
        {
            "rm -rf /",
            "rm -rf /*",
            "mkfs",
            "dd if=",
            ":(){ :|:& };:",  // Fork bomb
            "chmod -R 777 /",
            "format ",
            "del /s /q c:\\",
            "rd /s /q c:\\"
        };

        var normalizedCommand = command.Trim().ToLowerInvariant();

        return dangerousPatterns.Any(pattern =>
            normalizedCommand.Contains(pattern, StringComparison.OrdinalIgnoreCase));
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
