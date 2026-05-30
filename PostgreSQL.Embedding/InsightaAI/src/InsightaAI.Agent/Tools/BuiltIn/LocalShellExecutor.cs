using System.Diagnostics;
using System.Runtime.InteropServices;
using InsightaAI.LLM.Abstractions;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// 本地 Shell 命令执行器
/// 支持 Windows (CMD/PowerShell) 和 Linux (Bash)
/// </summary>
public class LocalShellExecutor : IShellExecutor
{
    private readonly bool _isWindows;

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

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            using var process = new Process { StartInfo = startInfo };

            var stdoutTcs = new TaskCompletionSource<string>();
            var stderrTcs = new TaskCompletionSource<string>();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    stdoutTcs.TrySetResult("");
                else
                    stdoutTcs.TrySetResult(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    stderrTcs.TrySetResult("");
                else
                    stderrTcs.TrySetResult(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // 等待进程完成或取消
            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(true);
                }
                catch { }
            });

            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTcs.Task;
            var stderr = await stderrTcs.Task;

            stopwatch.Stop();

            return new ShellResult
            {
                ExitCode = process.ExitCode,
                Stdout = stdout,
                Stderr = stderr,
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new ShellResult
            {
                ExitCode = -1,
                Stderr = "Command was cancelled",
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ShellResult
            {
                ExitCode = -1,
                Stderr = $"Failed to execute command: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
    }

    private (string fileName, string arguments) GetShellCommand(string command)
    {
        if (_isWindows)
        {
            // 使用 CMD 执行
            return ("cmd.exe", $"/c {command}");
        }
        else
        {
            // 使用 Bash 执行
            return ("/bin/bash", $"-c \"{EscapeBashArg(command)}\"");
        }
    }

    private static string EscapeBashArg(string arg)
    {
        return arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
