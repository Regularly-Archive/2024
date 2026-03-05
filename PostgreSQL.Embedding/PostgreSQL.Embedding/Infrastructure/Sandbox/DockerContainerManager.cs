using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PostgreSQL.Embedding.Infrastructure.Sandbox;

/// <summary>
/// Docker 容器管理器 - 使用 Docker SDK
/// </summary>
public class DockerContainerManager
{
    private readonly SandboxOptions _options;
    private readonly ILogger<DockerContainerManager> _logger;
    private readonly DockerClient _client;

    public DockerContainerManager(
        IOptions<SandboxOptions> options,
        ILogger<DockerContainerManager> logger)
    {
        _options = options.Value;
        _logger = logger;

        // 创建 Docker 客户端
        var dockerUri = _options.DockerUri;
        if (string.IsNullOrEmpty(dockerUri))
        {
            // 根据操作系统选择默认 URI
            // Windows: 优先尝试 TCP (Docker Desktop 已暴露 2375)，否则用 named pipe
            // Linux: 使用 Unix socket
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // 使用 TCP 连接 WSL2 里的 Docker (需要先在 WSL2 里运行 socat)
                dockerUri = "tcp://localhost:2375";
            }
            else
            {
                dockerUri = "unix:///var/run/docker.sock";
            }


        }

        _client = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
    }

    /// <summary>
    /// 创建并启动容器
    /// </summary>
    public async Task<string> CreateContainerAsync(
        string sessionId,
        Dictionary<string, string> volumeMappings,
        CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var containerId = $"{sessionId}-container";

        // 检查容器是否已存在
        var sw = Stopwatch.StartNew();
        try
        {
            var existingContainer = await _client.Containers.InspectContainerAsync(containerId, cancellationToken);
            // 容器已存在，先删除
            await _client.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true }, cancellationToken);
            _logger.LogInformation("[Docker] Remove existing container: {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }
        catch (DockerContainerNotFoundException)
        {
            // 容器不存在，正常继续
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Docker] Error checking container, continuing...");
        }

        // 拉取镜像（如果需要）
        sw.Restart();
        await PullImageIfNeededAsync(_options.DefaultImage, cancellationToken);
        _logger.LogInformation("[Docker] Pull/verify image: {ElapsedMs}ms", sw.ElapsedMilliseconds);

        // 构建卷映射 (使用字符串格式 "source:target:mode")
        // 自动转换路径格式：Windows 路径 → WSL2/Linux 路径
        var binds = volumeMappings.Select(kv => $"{ConvertToContainerPath(kv.Key)}:{kv.Value}:rw").ToList();

        // 构建资源限制
        var hostConfig = new HostConfig
        {
            Binds = binds,
            Memory = _options.MemoryLimitMb.HasValue ? (long)(_options.MemoryLimitMb.Value * 1024 * 1024) : 0,
            NanoCPUs = _options.CpuLimit.HasValue ? (long)(_options.CpuLimit.Value * 1_000_000_000) : 0
        };

        // 创建容器
        var createParams = new CreateContainerParameters
        {
            Name = containerId,
            Image = _options.DefaultImage,
            Cmd = new[] { "sleep", "infinity" },
            WorkingDir = _options.WorkingDirectory,
            HostConfig = hostConfig,
            Tty = false,
            AttachStdout = false,
            AttachStderr = false
        };

        sw.Restart();
        var createResponse = await _client.Containers.CreateContainerAsync(createParams, cancellationToken);
        _logger.LogInformation("[Docker] Create container: {ElapsedMs}ms", sw.ElapsedMilliseconds);

        // 启动容器
        sw.Restart();
        await _client.Containers.StartContainerAsync(createResponse.ID, new ContainerStartParameters(), cancellationToken);
        _logger.LogInformation("[Docker] Start container: {ElapsedMs}ms", sw.ElapsedMilliseconds);

        totalStopwatch.Stop();
        _logger.LogInformation("Container {ContainerId} started for session {SessionId}, total time: {TotalMs}ms",
            containerId, sessionId, totalStopwatch.ElapsedMilliseconds);

        return containerId;
    }

    /// <summary>
    /// 执行命令
    /// </summary>
    public async Task<CommandResult> ExecuteCommandAsync(
        string containerId,
        string command,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        // 使用 sh -c 执行命令
        var execCreateParams = new ContainerExecCreateParameters
        {
            Cmd = new[] { "sh", "-c", command },
            AttachStdout = true,
            AttachStderr = true,
            WorkingDir = _options.WorkingDirectory
        };

        var execCreate = await _client.Exec.ExecCreateContainerAsync(containerId, execCreateParams, cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.CommandTimeout);

        var stream = await _client.Exec.StartAndAttachContainerExecAsync(execCreate.ID, false, cts.Token);

        var (stdout, stderr) = await ReadOutputAsync(stream, cts.Token);

        // 检查是否超时
        if (cts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return new CommandResult
            {
                ExitCode = -1,
                Stdout = stdout,
                Stderr = "Command timed out"
            };
        }

        // 获取退出码
        var inspect = await _client.Exec.InspectContainerExecAsync(execCreate.ID, cancellationToken);

        sw.Stop();
        _logger.LogInformation("[Docker] Execute command in {ContainerId}: {ElapsedMs}ms, exitCode={ExitCode}",
            containerId, sw.ElapsedMilliseconds, inspect.ExitCode);

        return new CommandResult
        {
            ExitCode = (int)inspect.ExitCode,
            Stdout = stdout,
            Stderr = stderr
        };
    }

    /// <summary>
    /// 销毁容器
    /// </summary>
    public async Task DisposeContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            await _client.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true }, cancellationToken);
            _logger.LogInformation("[Docker] Dispose container {ContainerId}: {ElapsedMs}ms", containerId, sw.ElapsedMilliseconds);
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
        var sw = Stopwatch.StartNew();
        try
        {
            var inspect = await _client.Containers.InspectContainerAsync(containerId, cancellationToken);
            var isRunning = inspect.State.Running;
            _logger.LogInformation("[Docker] IsContainerRunning: {ElapsedMs}ms, running={IsRunning}", sw.ElapsedMilliseconds, isRunning);
            return isRunning;
        }
        catch (DockerContainerNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Docker] IsContainerRunning error");
            return false;
        }
    }

    /// <summary>
    /// 将本地路径转换为容器能识别的路径
    /// 自动适配不同环境：Windows → WSL2/Linux 路径
    /// </summary>
    private static string ConvertToContainerPath(string localPath)
    {
        if (string.IsNullOrEmpty(localPath))
            return localPath;

        // 如果已经是 Unix 路径，直接返回
        if (localPath.StartsWith("/"))
            return localPath;

        // Windows 路径转换
        // C:\Users\... → /mnt/c/Users/...
        // D:\... → /mnt/d/...
        if (localPath.Length >= 2 && localPath[1] == ':')
        {
            var driveLetter = localPath[0].ToString().ToLower();
            var pathWithoutDrive = localPath[2..];
            // 替换反斜杠为正斜杠
            pathWithoutDrive = pathWithoutDrive.Replace('\\', '/');
            return $"/mnt/{driveLetter}{pathWithoutDrive}";
        }

        return localPath;
    }

    /// <summary>
    /// 拉取镜像（如果不存在）
    /// </summary>
    private async Task PullImageIfNeededAsync(string image, CancellationToken cancellationToken)
    {
        try
        {
            // 检查镜像是否存在
            await _client.Images.InspectImageAsync(image, cancellationToken);
            _logger.LogDebug("Image {Image} already exists", image);
        }
        catch (DockerImageNotFoundException)
        {
            // 镜像不存在，需要拉取
            _logger.LogInformation("Pulling image {Image}...", image);

            await _client.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = image },
                null,
                new Progress<JSONMessage>(m =>
                {
                    if (!string.IsNullOrEmpty(m.Status))
                    {
                        _logger.LogDebug("[Docker] Pull progress: {Status} {Progress}", m.Status, m.Progress);
                    }
                }),
                cancellationToken);

            _logger.LogInformation("Image {Image} pulled successfully", image);
        }
    }

    /// <summary>
    /// 读取流输出
    /// </summary>
    private static async Task<(string stdout, string stderr)> ReadOutputAsync(MultiplexedStream stream, CancellationToken cancellationToken)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var buffer = new byte[8192];

        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken);
            if (result.EOF || result.Count == 0)
                break;

            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            if (result.Target == MultiplexedStream.TargetStream.StandardOut)
                stdout.Append(text);
            else
                stderr.Append(text);
        }

        return (stdout.ToString(), stderr.ToString());
    }
}

/// <summary>
/// 命令执行结果
/// </summary>
public class CommandResult
{
    public int ExitCode { get; set; }
    public string Stdout { get; set; } = string.Empty;
    public string Stderr { get; set; } = string.Empty;
    public bool Success => ExitCode == 0;

    public override string ToString() => $"ExitCode: {ExitCode}\nStdout: {Stdout}\nStderr: {Stderr}";
}
