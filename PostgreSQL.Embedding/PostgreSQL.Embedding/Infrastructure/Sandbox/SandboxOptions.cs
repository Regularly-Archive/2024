namespace PostgreSQL.Embedding.Infrastructure.Sandbox;

/// <summary>
/// Docker Sandbox 配置选项
/// </summary>
public class SandboxOptions
{
    /// <summary>
    /// Docker 可执行文件路径
    /// </summary>
    public string DockerPath { get; set; } = "docker";

    /// <summary>
    /// 默认镜像名称
    /// </summary>
    public string DefaultImage { get; set; } = "alpine:latest";

    /// <summary>
    /// 容器工作目录
    /// </summary>
    public string WorkingDirectory { get; set; } = "/sandbox";

    /// <summary>
    /// 会话超时时间 (最后活跃时间后多久销毁)
    /// </summary>
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 容器最大生命周期 (创建后多久强制销毁)
    /// </summary>
    public TimeSpan MaxLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// 清理检查间隔
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// CPU 限制 (核心数)
    /// </summary>
    public double? CpuLimit { get; set; }

    /// <summary>
    /// 内存限制 (MB)
    /// </summary>
    public long? MemoryLimitMb { get; set; }

    /// <summary>
    /// 命令执行超时时间
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
