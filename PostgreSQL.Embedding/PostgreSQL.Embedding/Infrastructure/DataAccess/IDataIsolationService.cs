namespace PostgreSQL.Embedding.Infrastructure.DataAccess
{
    /// <summary>
    /// 数据隔离服务接口
    /// </summary>
    public interface IDataIsolationService
    {
        /// <summary>
        /// 获取当前用户的标识（用户名或用户ID）
        /// </summary>
        string? GetCurrentUserId();

        /// <summary>
        /// 判断当前用户是否为管理员（可访问所有数据）
        /// </summary>
        bool IsAdmin();

        /// <summary>
        /// 判断指定类型是否需要数据隔离
        /// </summary>
        bool ShouldIsolate(Type entityType);
    }
}
