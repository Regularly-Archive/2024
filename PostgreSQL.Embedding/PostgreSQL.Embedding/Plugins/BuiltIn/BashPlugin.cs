using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Infrastructure.Sandbox;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PostgreSQL.Embedding.Plugins.BuiltIn;

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
        var result = await ExecuteCommandInSandboxAsync(command, kernel);
        var ret = result.ExitCode == 0 ? result.Stdout : result.Stderr;
        if (ret.Length > 1500)
        {
            return $@"The output of the current command has exceeded the context window length. Please use the following options as alternatives:
            - `bash('{command} | tail -n 100')` - last 100 lines
            - `bash('{command} | head -n 100')` - first 100 lines
            - `bash('{command} | grep KEYWORD')` - filter by keyword
            - `bash('{command} > /sandbox/tmp/out.txt')` - save to file, then read
            ";
        }
        else
        {
            return ret;
        }
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

        // 获取卷映射
        var volumeMappings = sandboxContext.GetVolumeMappings();

        // 获取或创建会话
        var session = await _sandboxService!.GetOrCreateSessionAsync(sessionId, volumeMappings);

        _logger.LogInformation("Executing in Docker sandbox session {SessionId}: {Command}", sessionId, command);

        // 执行命令
        var result = await _sandboxService.ExecuteAsync(sessionId, command);

        return result;
    }

}
