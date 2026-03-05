using System.Diagnostics;
using Microsoft.Extensions.Options;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Infrastructure.DataAccess;

namespace PostgreSQL.Embedding.Infrastructure.Sandbox;

/// <summary>
/// 沙箱服务 - 管理会话生命周期和命令执行
/// </summary>
public class SandboxService
{
    private readonly DockerContainerManager _containerManager;
    private readonly SandboxOptions _options;
    private readonly ILogger<SandboxService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public SandboxService(
        DockerContainerManager containerManager,
        IOptions<SandboxOptions> options,
        ILogger<SandboxService> logger,
        IServiceProvider serviceProvider)
    {
        _containerManager = containerManager;
        _options = options.Value;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 获取仓储（每次创建新作用域）
    /// </summary>
    private IRepository<SandboxSession> GetRepository()
    {
        using var scope = _serviceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IRepository<SandboxSession>>();
    }

    /// <summary>
    /// 获取或创建会话
    /// </summary>
    public async Task<SandboxSession> GetOrCreateSessionAsync(
        string sessionId,
        Dictionary<string, string> volumeMappings,
        CancellationToken cancellationToken = default)
    {
        var repo = GetRepository();

        // 先检查数据库中是否存在
        var existingSession = await repo.FindAsync(x => x.SessionId == sessionId);

        if (existingSession != null)
        {
            // 检查容器是否还在运行
            if (await _containerManager.IsContainerRunningAsync(existingSession.ContainerId, cancellationToken))
            {
                // 更新活跃时间和过期时间
                existingSession.Touch();
                existingSession.ExpiresAt = DateTime.UtcNow.Add(_options.MaxLifetime);
                await repo.UpdateAsync(existingSession);

                _logger.LogDebug("Reusing existing session {SessionId}", sessionId);
                return existingSession;
            }

            // 容器已不在，删除旧会话记录
            await repo.DeleteAsync(x => x.SessionId == sessionId);
            _logger.LogWarning("Container was not running for session {SessionId}, recreating", sessionId);
        }

        // 创建新会话
        var containerId = await _containerManager.CreateContainerAsync(sessionId, volumeMappings, cancellationToken);

        var session = new SandboxSession
        {
            SessionId = sessionId,
            ContainerId = containerId,
            Status = SandboxSessionStatus.Running,
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(_options.MaxLifetime),
            LocalPath = volumeMappings.Keys.FirstOrDefault() ?? "",
            ContainerWorkDir = _options.WorkingDirectory
        };

        await repo.AddAsync(session);
        _logger.LogInformation("Created new session {SessionId} with container {ContainerId}",
            sessionId, containerId);

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
        var repo = GetRepository();
        var session = await repo.FindAsync(x => x.SessionId == sessionId);
        if (session == null)
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
        session.ExpiresAt = DateTime.UtcNow.Add(_options.MaxLifetime);
        await repo.UpdateAsync(session);

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
        var repo = GetRepository();
        var session = await repo.FindAsync(x => x.SessionId == sessionId);
        if (session != null)
        {
            _logger.LogInformation("Disposing session {SessionId}", sessionId);

            session.Status = SandboxSessionStatus.Disposed;
            await repo.UpdateAsync(session);

            await _containerManager.DisposeContainerAsync(session.ContainerId, cancellationToken);
        }
    }

    /// <summary>
    /// 获取过期的会话（从数据库查询）
    /// </summary>
    public async Task<List<SandboxSession>> GetExpiredSessionsAsync()
    {
        var repo = GetRepository();
        var now = DateTime.UtcNow;
        var idleThreshold = now.AddMinutes(-5);
        return await repo.FindListAsync(x =>
            x.ExpiresAt < now ||
            x.LastActiveAt < idleThreshold);
    }

    /// <summary>
    /// 获取所有会话
    /// </summary>
    public async Task<List<SandboxSession>> GetAllSessionsAsync()
    {
        var repo = GetRepository();
        return await repo.GetAllAsync();
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
