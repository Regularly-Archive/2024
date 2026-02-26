using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace PostgreSQL.Embedding.Infrastructure.Sandbox;

/// <summary>
/// Docker 容器管理器 - 通过 CLI 调用 Docker
/// </summary>
public class DockerContainerManager
{
    private readonly SandboxOptions _options;
    private readonly ILogger<DockerContainerManager> _logger;

    public DockerContainerManager(
        IOptions<SandboxOptions> options,
        ILogger<DockerContainerManager> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 创建并启动容器
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="volumeMappings">卷映射字典：本地路径 -> 容器内路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>容器 ID</returns>
    public async Task<string> CreateContainerAsync(
        string sessionId,
        Dictionary<string, string> volumeMappings,
        CancellationToken cancellationToken = default)
    {
        var containerId = $"{sessionId}-container";

        // 构建 CPU 限制参数
        var cpuArgs = _options.CpuLimit.HasValue
            ? $"--cpus={_options.CpuLimit.Value}"
            : "";

        // 构建内存限制参数
        var memoryArgs = _options.MemoryLimitMb.HasValue
            ? $"--memory={_options.MemoryLimitMb.Value}m"
            : "";

        // 检查容器是否已存在
        var inspectResult = await RunDockerCommandAsync(
            $"inspect {containerId}",
            cancellationToken: cancellationToken);

        if (inspectResult.ExitCode == 0)
        {
            // 容器已存在，先删除
            await RunDockerCommandAsync(
                $"rm -f {containerId}",
                cancellationToken: cancellationToken);
        }

        // 拉取镜像（如果需要）
        await RunDockerCommandAsync(
            $"pull {_options.DefaultImage}",
            cancellationToken: cancellationToken);

        var volumeArgs = volumeMappings
            .Select(kv => $"-v {kv.Key}:{kv.Value}");

        // 创建并启动容器
        var createCommand = $"run -d " +
            $"--name {containerId} " +
            $"{cpuArgs} {memoryArgs} " +
            string.Join(" ", volumeArgs) + " " +
            $"--workdir {_options.WorkingDirectory} " +
            $"{_options.DefaultImage} sleep infinity";

        var createResult = await RunDockerCommandAsync(
            createCommand,
            cancellationToken: cancellationToken);

        if (createResult.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to create container: {createResult.Stderr}");
        }

        _logger.LogInformation("Container {ContainerId} started for session {SessionId}", containerId, sessionId);

        return containerId;
    }

    /// <summary>
    /// 执行命令 (Agent 当前目录是 /workspace)
    /// </summary>
    public async Task<CommandResult> ExecuteCommandAsync(
        string containerId,
        string command,
        CancellationToken cancellationToken = default)
    {
        var escapedCommand = command.Replace("\"", "\\\"");
        var execCommand = $"exec {containerId} sh -c \"{escapedCommand}\"";

        return await RunDockerCommandAsync(
            execCommand,
            _options.CommandTimeout,
            cancellationToken);
    }

    /// <summary>
    /// 销毁容器
    /// </summary>
    public async Task DisposeContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        try
        {
            await RunDockerCommandAsync(
                $"rm -f {containerId}",
                cancellationToken: cancellationToken);

            _logger.LogInformation("Container {ContainerId} disposed", containerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose container {ContainerId}", containerId);
        }
    }

    /// <summary>
    /// 检查容器是否存在并运行
    /// </summary>
    public async Task<bool> IsContainerRunningAsync(string containerId, CancellationToken cancellationToken = default)
    {
        var result = await RunDockerCommandAsync(
            $"inspect -f '{{{{.State.Running}}}}' {containerId}",
            cancellationToken: cancellationToken);

        return result.ExitCode == 0 && result.Stdout.Replace("\n","").Trim() == "'true'";
    }

    /// <summary>
    /// 运行 Docker 命令
    /// </summary>
    private async Task<CommandResult> RunDockerCommandAsync(
        string arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _options.DockerPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory
            }
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        if (timeout.HasValue)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
                return new CommandResult
                {
                    ExitCode = -1,
                    Stdout = stdout,
                    Stderr = "Command timed out"
                };
            }
        }
        else
        {
            await process.WaitForExitAsync(cancellationToken);
        }

        return new CommandResult
        {
            ExitCode = process.ExitCode,
            Stdout = stdout,
            Stderr = stderr
        };
    }
}

/// <summary>
/// 命令执行结果
/// </summary>
public class CommandResult
{
    [JsonPropertyName("exit_code")]
    public int ExitCode { get; set; }

    [JsonPropertyName("std_out")]
    public string Stdout { get; set; } = string.Empty;

    [JsonPropertyName("std_out")]
    public string Stderr { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success => ExitCode == 0;

    public override string ToString() => $"ExitCode: {ExitCode}\nStdout: {Stdout}\nStderr: {Stderr}";
}
