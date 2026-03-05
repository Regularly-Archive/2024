using SqlSugar;

namespace PostgreSQL.Embedding.Domain.Entities
{
    /// <summary>
    /// 沙箱会话状态
    /// </summary>
    public enum SandboxSessionStatus
    {
        /// <summary>创建中</summary>
        Creating = 0,

        /// <summary>运行中</summary>
        Running = 1,

        /// <summary>空闲 (已有请求完成，等待超时)</summary>
        Idle = 2,

        /// <summary>已过期/待清理</summary>
        Expired = 3,

        /// <summary>已销毁</summary>
        Disposed = 4
    }

    /// <summary>
    /// 沙箱会话
    /// </summary>
    [SugarTable("sandbox_sessions")]
    public class SandboxSession : BaseEntity
    {
        /// <summary>
        /// 会话唯一标识
        /// </summary>
        [SugarColumn(ColumnName = "session_id", Length = 100)]
        public string SessionId { get; set; } = "";

        /// <summary>
        /// 容器 ID
        /// </summary>
        [SugarColumn(ColumnName = "container_id", Length = 100)]
        public string ContainerId { get; set; } = "";

        /// <summary>
        /// 会话状态
        /// </summary>
        [SugarColumn(ColumnName = "status")]
        public SandboxSessionStatus Status { get; set; } = SandboxSessionStatus.Creating;

        /// <summary>
        /// 会话创建时间
        /// </summary>
        [SugarColumn(ColumnName = "created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 最后活跃时间
        /// </summary>
        [SugarColumn(ColumnName = "last_active_at")]
        public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 预计过期时间 (TTL)
        /// </summary>
        [SugarColumn(ColumnName = "expires_at")]
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// 本地挂载路径
        /// </summary>
        [SugarColumn(ColumnName = "local_path", Length = 500)]
        public string LocalPath { get; set; } = "";

        /// <summary>
        /// 容器内工作目录
        /// </summary>
        [SugarColumn(ColumnName = "container_work_dir", Length = 500)]
        public string ContainerWorkDir { get; set; } = "";

        /// <summary>
        /// 是否过期
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;

        /// <summary>
        /// 是否空闲 (超过5分钟无活动)
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public bool IsIdle => (DateTime.UtcNow - LastActiveAt) > TimeSpan.FromMinutes(5);

        /// <summary>
        /// 更新活跃时间
        /// </summary>
        public void Touch()
        {
            LastActiveAt = DateTime.UtcNow;
            Status = SandboxSessionStatus.Running;
        }
    }
}
