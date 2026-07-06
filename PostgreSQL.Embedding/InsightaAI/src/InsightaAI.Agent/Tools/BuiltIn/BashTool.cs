using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// Shell 命令执行工具
/// 通过 IShellExecutor 接口支持多种执行环境（本地、Docker、远程等）
/// </summary>
public class BashTool : IToolExecutor
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
            Description = "执行 Shell 命令。适用于运行系统命令、脚本、编译代码等。" +
                         "在 Windows 上使用 PowerShell，在 Linux/Mac 上使用 Bash。" +
                         "Windows 平台请优先使用 PowerShell cmdlet，避免原生命令（中文输出可能乱码）。" +
                         "常用映射：ipconfig → Get-NetIPAddress, tasklist → Get-Process, dir → Get-ChildItem, " +
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
                        description = "要执行的 Shell 命令"
                    },
                    working_directory = new
                    {
                        type = "string",
                        description = "命令执行的工作目录。默认为当前目录。"
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

    /// <summary>
    /// 拦截大命令输出：保留头尾各 50 行
    /// </summary>
    public InterceptionResult Intercept(ToolResult result, TruncationContext context)
    {
        var text = result.Content.OfType<TextBlock>().FirstOrDefault()?.Text;
        if (text == null || context.OriginalLength <= 30_000)
            return InterceptionResult.NotIntercepted(result);

        var lines = text.Split('\n');
        if (lines.Length <= 100)
            return InterceptionResult.NotIntercepted(result);

        // 保留头尾各 50 行
        var head = lines.Take(50);
        var tail = lines.TakeLast(50);
        var truncated = string.Join("\n", head)
            + $"\n\n[... 截断 {lines.Length - 100} 行 ...]\n\n"
            + string.Join("\n", tail);

        return new InterceptionResult(
            ToolResult.FromText(truncated),
            toolResultIntercepted: true,
            originalLength: context.OriginalLength
        );
    }

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
