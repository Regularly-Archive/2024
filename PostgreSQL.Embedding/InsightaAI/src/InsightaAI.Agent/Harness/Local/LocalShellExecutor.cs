using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using InsightaAI.Agent.Abstractions;

namespace InsightaAI.Agent.Harness.Local;

/// <summary>
/// Shell 命令执行记录
/// </summary>
public sealed record ShellCommandLog
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public required string Command { get; init; }
    public string? WorkingDirectory { get; init; }
    public int ExitCode { get; init; }
    public TimeSpan Duration { get; init; }
    public bool Success => ExitCode == 0;
}

/// <summary>
/// 本地 Shell 命令执行器
/// 支持 Windows (PowerShell) 和 Linux (Bash)
/// </summary>
public class LocalShellExecutor : IShellExecutor
{
    private readonly bool _isWindows;

    /// <summary>
    /// 命令执行日志事件（用于审计）
    /// </summary>
    public static event Action<ShellCommandLog>? OnCommandExecuted;

    public LocalShellExecutor()
    {
        _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }

    public async Task<ShellResult> ExecuteAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var (fileName, arguments) = GetShellCommand(command);

            // 使用 UTF-8 编码
            var encoding = Encoding.UTF8;

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            using var process = new Process { StartInfo = startInfo };

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();
            var stdoutTcs = new TaskCompletionSource<bool>();
            var stderrTcs = new TaskCompletionSource<bool>();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    stdoutTcs.TrySetResult(true);
                else
                    stdoutBuilder.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    stderrTcs.TrySetResult(true);
                else
                    stderrBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // 等待进程完成或取消
            await using var reg = cancellationToken.Register(() =>
            {
                try { process.Kill(true); } catch { }
            });

            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdoutTcs.Task, stderrTcs.Task);

            stopwatch.Stop();

            var result = new ShellResult
            {
                ExitCode = process.ExitCode,
                Stdout = stdoutBuilder.ToString().TrimEnd(),
                Stderr = stderrBuilder.ToString().TrimEnd(),
                Duration = stopwatch.Elapsed
            };

            // 记录命令执行日志（审计）
            try
            {
                OnCommandExecuted?.Invoke(new ShellCommandLog
                {
                    Command = command,
                    WorkingDirectory = workingDirectory,
                    ExitCode = result.ExitCode,
                    Duration = result.Duration
                });
            }
            catch { /* 日志记录失败不影响主流程 */ }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ShellResult
            {
                ExitCode = -1,
                Stderr = $"执行命令时出错: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
    }

    private (string fileName, string arguments) GetShellCommand(string command)
    {
        if (_isWindows)
        {
            // 使用 PowerShell 执行，强制设置 UTF-8 编码
            var encodedCommand = $"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; {command}";
            return ("powershell.exe", $"-NoProfile -NonInteractive -Command \"{EscapePowerShellArg(encodedCommand)}\"");
        }
        else
        {
            // 使用 Bash 执行
            return ("/bin/bash", $"-c \"{EscapeBashArg(command)}\"");
        }
    }

    private static string EscapePowerShellArg(string arg)
    {
        return arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string EscapeBashArg(string arg)
    {
        return arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
