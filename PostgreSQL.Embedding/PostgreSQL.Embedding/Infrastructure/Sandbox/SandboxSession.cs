namespace PostgreSQL.Embedding.Infrastructure.Sandbox;

/// <summary>
/// 沙箱会话状态
/// </summary>
public enum SandboxSessionStatus
{
    /// <summary>创建中</summary>
    Creating,

    /// <summary>运行中</summary>
    Running,

    /// <summary>空闲 (已有请求完成，等待超时)</summary>
    Idle,

    /// <summary>已过期/待清理</summary>
    Expired,

    /// <summary>已销毁</summary>
    Disposed
}

/// <summary>
/// 沙箱会话信息
/// </summary>
public class SandboxSession
{
    /// <summary>
    /// 会话唯一标识 (对应 appId/sessionId)
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 容器 ID
    /// </summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>
    /// 会话状态
    /// </summary>
    public SandboxSessionStatus Status { get; set; } = SandboxSessionStatus.Creating;

    /// <summary>
    /// 会话创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后活跃时间
    /// </summary>
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 预计过期时间 (TTL)
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// 本地挂载路径
    /// </summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>
    /// 容器内工作目录
    /// </summary>
    public string ContainerWorkDir { get; set; } = string.Empty;

    /// <summary>
    /// 是否空闲 (用于超时判断)
    /// </summary>
    public bool IsIdle => (DateTime.UtcNow - LastActiveAt) > TimeSpan.FromMinutes(5);

    /// <summary>
    /// 是否过期 (TTL 判断)
    /// </summary>
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;

    /// <summary>
    /// 更新活跃时间
    /// </summary>
    public void Touch()
    {
        LastActiveAt = DateTime.UtcNow;
        Status = SandboxSessionStatus.Running;
    }
}
