using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace PostgreSQL.Embedding.Infrastructure.Sandbox;

/// <summary>
/// 沙箱服务 - 管理会话生命周期和命令执行
/// </summary>
public class SandboxService
{
    private readonly DockerContainerManager _containerManager;
    private readonly SandboxOptions _options;
    private readonly ILogger<SandboxService> _logger;

    /// <summary>
    /// 会话存储 (内存字典，生产环境可用 Redis)
    /// </summary>
    private readonly ConcurrentDictionary<string, SandboxSession> _sessions = new();

    public SandboxService(
        DockerContainerManager containerManager,
        IOptions<SandboxOptions> options,
        ILogger<SandboxService> logger)
    {
        _containerManager = containerManager;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 获取或创建会话
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="volumeMappings">卷映射字典：本地路径 -> 容器内路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>沙箱会话</returns>
    public async Task<SandboxSession> GetOrCreateSessionAsync(
        string sessionId,
        Dictionary<string, string> volumeMappings,
        CancellationToken cancellationToken = default)
    {
        // 先检查内存中是否存在
        if (_sessions.TryGetValue(sessionId, out var existingSession))
        {
            // 检查容器是否还在运行
            if (await _containerManager.IsContainerRunningAsync(existingSession.ContainerId, cancellationToken))
            {
                existingSession.Touch();
                _logger.LogDebug("Reusing existing session {SessionId}", sessionId);
                return existingSession;
            }

            // 容器已不在，移除旧会话
            _sessions.TryRemove(sessionId, out _);
            _logger.LogWarning("Container was not running for session {SessionId}, recreating", sessionId);
        }

        // 创建新会话
        var session = new SandboxSession
        {
            SessionId = sessionId,
            ContainerId = await _containerManager.CreateContainerAsync(sessionId, volumeMappings, cancellationToken),
            Status = SandboxSessionStatus.Running,
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(_options.MaxLifetime),
            LocalPath = volumeMappings.Keys.FirstOrDefault() ?? string.Empty,
            ContainerWorkDir = _options.WorkingDirectory
        };

        _sessions.TryAdd(sessionId, session);
        _logger.LogInformation("Created new session {SessionId} with container {ContainerId}",
            sessionId, session.ContainerId);

        return session;
    }

    /// <summary>
    /// 执行命令
    /// </summary>
    public async Task<CommandResult> ExecuteAsync(
        string sessionId,
        string command,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        if (session.Status == SandboxSessionStatus.Disposed)
        {
            throw new InvalidOperationException($"Session {sessionId} has been disposed");
        }

        _logger.LogDebug("Executing command in session {SessionId}: {Command}", sessionId, command);

        // 更新活跃时间
        session.Touch();

        // 执行命令
        var result = await _containerManager.ExecuteCommandAsync(
            session.ContainerId,
            command,
            cancellationToken);

        _logger.LogDebug("Command executed in session {SessionId}: ExitCode={ExitCode}",
            sessionId, result.ExitCode);

        return result;
    }

    /// <summary>
    /// 销毁会话
    /// </summary>
    public async Task DisposeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            _logger.LogInformation("Disposing session {SessionId}", sessionId);

            session.Status = SandboxSessionStatus.Disposed;
            await _containerManager.DisposeContainerAsync(session.ContainerId, cancellationToken);
        }
    }

    /// <summary>
    /// 获取所有会话
    /// </summary>
    public IReadOnlyDictionary<string, SandboxSession> GetAllSessions()
    {
        return _sessions;
    }

    /// <summary>
    /// 获取过期的会话
    /// </summary>
    public IEnumerable<SandboxSession> GetExpiredSessions()
    {
        return _sessions.Values.Where(s => s.IsExpired || s.IsIdle);
    }

    /// <summary>
    /// 检查 Docker 是否可用
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _options.DockerPath,
                    Arguments = "version --format '{{.Server.Version}}'",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync(cancellationToken);

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
