using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
    /// <summary>
    /// 命令黑名单 - 禁止执行的危险命令
    /// </summary>
    public static class CommandBlacklist
    {
        // 禁止的命令关键词（不区分大小写）
        public static readonly HashSet<string> ForbiddenCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            // 文件系统破坏命令
            "rm -rf", "rm -fr", "rm -rf /", "del", "del /s", "del /q", "rd /s", "rd /q",
            "format", "fdisk", "mkfs", "dd",

            // 系统修改命令
            "chmod", "chown", "passwd", "useradd", "userdel", "usermod",
            "systemctl", "service", "init", "shutdown", "reboot", "halt", "poweroff",

            // 网络相关（避免泄露信息）
            "wget", "curl", "nc", "netcat", "ssh", "scp", "ftp", "telnet",

            // 提权相关
            "sudo", "su", "doas",

            // 脚本注入
            "eval", "exec", "source",

            // 后台运行
            "nohup", "bg", "fg",

            // 其他危险命令
            "alias", "unalias", "export", "env", "export"
        };

        // Linux 到 Windows 命令映射
        private static readonly Dictionary<string, string> LinuxToWindowsCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ls"] = "dir",
            ["cat"] = "type",
            ["rm"] = "del",
            ["rmdir"] = "rmdir",
            ["mkdir"] = "mkdir",
            ["cp"] = "copy",
            ["mv"] = "move",
            ["clear"] = "cls",
            ["pwd"] = "echo %cd%",
            ["head"] = "powershell -Command \"Get-Content -TotalCount\"",
            ["tail"] = "powershell -Command \"Get-Content -Tail\"",
            ["grep"] = "findstr",
            ["find"] = "dir /s",
            ["touch"] = "echo. >",
            ["wc"] = "find /c /v \"\"",
            ["du"] = "dir /s",
            ["df"] = "wmic logicaldisk get size,freespace,name",
            ["free"] = "systeminfo | find \"Available Physical Memory\"",
            ["uptime"] = "net stats srv",
            ["date"] = "echo %date%",
            ["time"] = "echo %time%",
            ["hostname"] = "hostname",
            ["whoami"] = "whoami",
            ["id"] = "whoami /user",
            ["uname"] = "ver",
            ["man"] = "help",
            ["less"] = "more",
            ["more"] = "more",
            ["sort"] = "sort",
            ["uniq"] = "findstr /",
            ["diff"] = "fc",
            ["top"] = "tasklist",
            ["ps"] = "tasklist",
            ["kill"] = "taskkill",
            ["killall"] = "taskkill /f /im",
            ["ping"] = "ping",
            ["traceroute"] = "tracert",
            ["netstat"] = "netstat",
            ["ifconfig"] = "ipconfig",
            ["ip addr"] = "ipconfig",
            ["ssh"] = "plink",
            ["scp"] = "pscp",
            ["tar"] = "tar",
            ["zip"] = "powershell -Command \"Compress-Archive\"",
            ["unzip"] = "powershell -Command \"Expand-Archive\"",
        };

        /// <summary>
        /// 检查命令是否包含禁止的关键词
        /// </summary>
        public static bool ContainsForbiddenCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return true;

            var lowerCommand = command.ToLowerInvariant().Trim();

            foreach (var forbidden in ForbiddenCommands)
            {
                if (lowerCommand.Contains(forbidden.ToLowerInvariant()))
                    return true;
            }

            // 检查是否有明显的目录遍历
            if (lowerCommand.Contains("../") || lowerCommand.Contains("..\\"))
                return true;

            // 检查是否有管道到 shell 或其他命令
            if (Regex.IsMatch(lowerCommand, @"\|\s*\w+") || lowerCommand.Contains("> /dev/") || lowerCommand.Contains("2>&1"))
                return true;

            return false;
        }

        /// <summary>
        /// 将 Linux 命令转换为 Windows 命令（如果在 Windows 环境下）
        /// </summary>
        public static string ConvertToWindowsCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return command;

            // 提取命令和参数
            var parts = command.Split(new[] { ' ' }, 2);
            var cmd = parts[0].ToLowerInvariant();
            var args = parts.Length > 1 ? parts[1] : "";

            if (LinuxToWindowsCommands.TryGetValue(cmd, out var windowsCmd))
            {
                // 对于需要特殊处理的命令
                if (cmd == "head" || cmd == "tail")
                {
                    // 提取行数参数
                    var match = System.Text.RegularExpressions.Regex.Match(args, @"-n\s+(\d+)");
                    if (match.Success)
                    {
                        var lines = match.Groups[1].Value;
                        args = args.Replace(match.Value, "").Trim();
                        if (!string.IsNullOrEmpty(args))
                        {
                            return $"{windowsCmd} {lines} \"{args}\"";
                        }
                        return $"{windowsCmd} {lines}";
                    }
                }
                else if (cmd == "ls")
                {
                    // ls 命令可能带有很多参数，简化处理
                    if (args.Contains("-la") || args.Contains("-l"))
                    {
                        return $"dir \"{args.Replace("-la", "").Replace("-l", "").Replace("  ", " ").Trim()}\"";
                    }
                    return $"dir";
                }
                else if (cmd == "cat")
                {
                    return $"type \"{args}\"";
                }
                else if (cmd == "grep")
                {
                    return $"findstr \"{args}\"";
                }
                else if (cmd == "find")
                {
                    // find /path -name "pattern" - simplified, just use dir /s
                    return "dir /s /b " + args.Trim('"', '\'');
                }
                else if (cmd == "touch")
                {
                    // touch filename
                    return $"echo. > \"{args}\"";
                }
                else if (cmd == "ps")
                {
                    return $"tasklist";
                }
                else if (cmd == "kill")
                {
                    // kill PID
                    return $"taskkill /PID {args}";
                }
                else if (cmd == "clear")
                {
                    return $"cls";
                }

                return windowsCmd + (string.IsNullOrEmpty(args) ? "" : " " + args);
            }

            return command;
        }
    }

    [KernelPluginAttribute(Description = "安全命令执行插件。在沙箱目录中执行预定义的可信命令，支持 Windows CMD/PowerShell 和 Linux Bash。危险命令已被列入黑名单禁止执行。", Version = "1.0")]
    public class BashPlugin : BasePlugin
    {
        private readonly ILogger<BashPlugin> _logger;
        private readonly bool _isWindows;

        public BashPlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _isWindows = OperatingSystem.IsWindows();
            _logger = _serviceProvider.GetService<ILoggerFactory>().CreateLogger<BashPlugin>();
        }

        /// <summary>
        /// 获取当前沙箱目录
        /// </summary>
        [KernelFunction]
        [Description("获取当前允许执行命令的工作目录")]
        public string GetSandboxDirectory(Kernel kernel)
        {
            var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();
            return sandboxContext.ToLinuxStyleRelativePath(sandboxContext.SessionDir, sandboxContext.SessionDir);
        }

        /// <summary>
        /// 执行只读命令（推荐）
        /// </summary>
        [KernelFunction]
        [Description("执行安全的只读命令，如 ls, dir, cat, type, head, tail, grep, findstr, pwd, echo 等。命令将在沙箱目录下执行。在 Windows 环境下，Linux 命令（如 ls, cat, grep）会自动转换为对应的 Windows 命令（dir, type, findstr）。")]
        public async Task<string> ExecuteReadOnlyCommandAsync(
            [Description("要执行的命令，例如 'ls -la', 'dir', 'cat filename.txt'。在 Windows 上使用 Linux 格式的命令会自动转换。")] string command, Kernel kernel)
        {
            if (CommandBlacklist.ContainsForbiddenCommand(command))
            {
                return $"错误：命令包含禁止执行的关键词。\n命令: {command}";
            }

            return await ExecuteCommandAsync(command, kernel);
        }

        /// <summary>
        /// 执行写命令（谨慎使用）
        /// </summary>
        [KernelFunction]
        [Description("执行可能修改文件的命令，如 echo, touch, mkdir, copy, move 等。在 Windows 环境下，Linux 命令会自动转换为对应的 Windows 命令。危险命令仍会被阻止。")]
        public async Task<string> ExecuteWriteCommandAsync(
            [Description("要执行的写命令，例如 'echo Hello > test.txt', 'mkdir newdir', 'touch file.txt'。在 Windows 上使用 Linux 格式的命令会自动转换。")] string command, Kernel kernel)
        {
            if (CommandBlacklist.ContainsForbiddenCommand(command))
            {
                return $"错误：命令包含禁止执行的关键词。\n命令: {command}";
            }

            // 额外的写命令检查
            var lowerCommand = command.ToLowerInvariant();
            if (lowerCommand.Contains(">") || lowerCommand.Contains(">>") || lowerCommand.Contains("2>") || lowerCommand.Contains("2>>"))
            {
                _logger.LogWarning("Write command executed: {Command}", command);
            }

            return await ExecuteCommandAsync(command, kernel);
        }

        /// <summary>
        /// 列出目录内容
        /// </summary>
        [KernelFunction]
        [Description("列出目录中的文件和子目录，支持指定路径。")]
        public async Task<string> ListDirectoryAsync(
            Kernel kernel,
            [Description("要列出的目录路径，默认为沙箱目录")] string? path = null)
        {
            var targetPath = NormalizePath(path ?? ".", kernel);
            var isWindows = OperatingSystem.IsWindows();
            var command = isWindows ? $"dir \"{targetPath}\"" : $"ls -la \"{targetPath}\"";

            return await ExecuteCommandAsync(command, kernel);
        }

        /// <summary>
        /// 读取文本文件内容（便捷方法）
        /// </summary>
        [KernelFunction]
        [Description("读取文本文件的前N行内容，N默认为20行。")]
        public async Task<string> ReadFileHeadAsync(
            [Description("要读取的文件路径")] string filePath,
            Kernel kernel,
            [Description("要读取的行数，默认为20")] int lines = 20)
        {
            var targetPath = NormalizePath(filePath, kernel);
            var command = OperatingSystem.IsWindows()
                ? $"powershell -Command \"Get-Content -Path '{targetPath}' -TotalCount {lines}\""
                : $"head -n {lines} \"{targetPath}\"";

            return await ExecuteCommandAsync(command, kernel);
        }

        /// <summary>
        /// 读取文本文件末尾（便捷方法）
        /// </summary>
        [KernelFunction]
        [Description("读取文本文件的末尾N行内容，N默认为20行。")]
        public async Task<string> ReadFileTailAsync(
            [Description("要读取的文件路径")] string filePath,
            Kernel kernel,
            [Description("要读取的行数，默认为20")] int lines = 20
            )
        {
            var targetPath = NormalizePath(filePath, kernel);
            var command = OperatingSystem.IsWindows()
                ? $"powershell -Command \"Get-Content -Path '{targetPath}' -Tail {lines}\""
                : $"tail -n {lines} \"{targetPath}\"";

            return await ExecuteCommandAsync(command, kernel);
        }

        private string NormalizePath(string path, Kernel kernel)
        {
            var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();

            if (string.IsNullOrWhiteSpace(path))
                return sandboxContext.SessionDir;

            // 处理相对路径
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(sandboxContext.SessionDir, path);
            }

            // 确保路径在沙箱目录内
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(sandboxContext.SessionDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException($"路径超出沙箱目录范围: {path}");
            }

            return fullPath;
        }

        private async Task<string> ExecuteCommandAsync(string command, Kernel kernel)
        {
            try
            {
                // 在 Windows 下自动转换 Linux 命令
                if (_isWindows)
                {
                    command = CommandBlacklist.ConvertToWindowsCommand(command);
                }

                var shell = _isWindows ? "cmd.exe" : "/bin/bash";
                var shellArgs = _isWindows ? $"/c \"{command}\"" : $"-c \"{command}\"";

                _logger.LogInformation("Executing command: {Command}", command);

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = shell,
                        Arguments = shellArgs,
                        WorkingDirectory = GetSandboxDirectory(kernel),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        Environment =
                        {
                            ["HOME"] = _isWindows ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : "/tmp",
                            ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "",

                        }
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                var result = $"命令: {command}\n工作目录: {GetSandboxDirectory(kernel)}\n";

                if (!string.IsNullOrWhiteSpace(output))
                    result += $"\n输出:\n{output}";

                if (!string.IsNullOrWhiteSpace(error))
                    result += $"\n错误:\n{error}";

                result += $"\n退出码: {process.ExitCode}";

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Command execution failed: {Command}", command);
                return $"命令执行失败: {ex.Message}\n命令: {command}";
            }
        }
    }
}
