using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace PostgreSQL.Embedding.Infrastructure.Sandbox;

/// <summary>
/// 沙箱后台清理服务 - 负责 TTL 管理和资源清理
/// </summary>
public class SandboxCleanupService : BackgroundService
{
    private readonly SandboxService _sandboxService;
    private readonly SandboxOptions _options;
    private readonly ILogger<SandboxCleanupService> _logger;

    public SandboxCleanupService(
        SandboxService sandboxService,
        IOptions<SandboxOptions> options,
        ILogger<SandboxCleanupService> logger)
    {
        _sandboxService = sandboxService;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Sandbox cleanup service started with interval {Interval}",
            _options.CleanupInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.CleanupInterval, stoppingToken);
                await CleanupExpiredSessionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // 正常退出
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup iteration");
            }
        }

        _logger.LogInformation("Sandbox cleanup service stopped");
    }

    /// <summary>
    /// 清理过期会话
    /// </summary>
    private async Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken)
    {
        var expiredSessions = await _sandboxService.GetExpiredSessionsAsync();

        if (expiredSessions.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Found {Count} expired sessions to cleanup", expiredSessions.Count);

        foreach (var session in expiredSessions)
        {
            try
            {
                await _sandboxService.DisposeSessionAsync(session.SessionId, cancellationToken);
                _logger.LogInformation("Cleaned up expired session {SessionId}", session.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cleanup session {SessionId}", session.SessionId);
            }
        }
    }

    /// <summary>
    /// 服务启动时清理一次
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sandbox cleanup service starting...");
        await base.StartAsync(cancellationToken);
    }

    /// <summary>
    /// 服务停止时清理所有会话
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sandbox cleanup service stopping, disposing all sessions...");

        var sessions = await _sandboxService.GetAllSessionsAsync();
        foreach (var session in sessions)
        {
            try
            {
                await _sandboxService.DisposeSessionAsync(session.SessionId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispose session {SessionId} on shutdown", session.SessionId);
            }
        }

        await base.StopAsync(cancellationToken);
    }
}
