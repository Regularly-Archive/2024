using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Infrastructure.Sandbox;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
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

    }

    [KernelPluginAttribute(Description = "安全命令执行插件。在沙箱目录中执行预定义的可信命令，支持 Windows CMD/PowerShell 和 Linux Bash。危险命令已被列入黑名单禁止执行。", Version = "1.0")]
    public class BashPlugin : BasePlugin
    {
        private readonly ILogger<BashPlugin> _logger;
        private readonly bool _isWindows;
        private readonly SandboxService? _sandboxService;
        private readonly bool _dockerAvailable;

        public BashPlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _isWindows = OperatingSystem.IsWindows();
            _logger = _serviceProvider.GetService<ILoggerFactory>().CreateLogger<BashPlugin>();

            // 尝试获取 SandboxService
            _sandboxService = _serviceProvider.GetService<SandboxService>();
            _dockerAvailable = _sandboxService != null;
        }

        /// <summary>
        /// 获取当前沙箱目录
        /// </summary>
        [KernelFunction]
        [Description("获取当前允许执行命令的工作目录")]
        public string GetSandboxDirectory(Kernel kernel)
        {
            return "/sandbox";
        }

        /// <summary>
        /// 执行只读命令（推荐）
        /// </summary>
        [KernelFunction]
        [Description("执行命令，如 ls, dir, cat, type, head, tail, grep, findstr, pwd, echo 等。命令将在沙箱目录下执行。")]
        public async Task<string> ExecuteCommandAsync(
            [Description("要执行的命令，例如 'ls -la', 'dir', 'cat filename.txt'")] string command, Kernel kernel)
        {
            if (CommandBlacklist.ContainsForbiddenCommand(command))
                throw new ArgumentException($"Detected restricted command: {command}.");

            var result = await ExecuteCommandInSandboxAsync(command, kernel);
            return result.ExitCode == 0 ? result.Stdout : result.Stderr;
        }


        /// <summary>
        /// 在 Docker 沙箱中执行命令
        /// </summary>
        private async Task<CommandResult> ExecuteCommandInSandboxAsync(string command, Kernel kernel)
        {
            var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();

            // 构建会话 ID: conversationId (从 SessionDir 中提取)
            // SessionDir = BaseDir/appId/conversationId
            var sessionId = Path.GetFileName(sandboxContext.SessionDir);
            var localPath = sandboxContext.RunDir;

            // 获取或创建会话
            var session = await _sandboxService!.GetOrCreateSessionAsync(sessionId, localPath);

            _logger.LogInformation("Executing in Docker sandbox session {SessionId}: {Command}", sessionId, command);

            // 执行命令
            var result = await _sandboxService.ExecuteAsync(sessionId, command);

            return result;
        }

    }
}
