using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.User;

namespace PostgreSQL.Embedding.Infrastructure.UserIdentity
{
    /// <summary>
    /// 当前用户服务接口
    /// </summary>
    public interface ICurrentUserService
    {
        /// <summary>
        /// 获取当前登录用户信息
        /// </summary>
        Task<SystemUser?> GetCurrentUserAsync();

        /// <summary>
        /// 获取当前用户身份信息（不含敏感信息）
        /// </summary>
        Task<UserIdentityInfo?> GetCurrentIdentityAsync();

        /// <summary>
        /// 根据用户ID获取用户
        /// </summary>
        Task<SystemUser?> GetByIdAsync(long userId);

        /// <summary>
        /// 更新用户资料
        /// </summary>
        Task UpdateProfileAsync(UpdateProfileRequest request);

        /// <summary>
        /// 判断当前用户是否为管理员
        /// </summary>
        bool IsAdmin();
    }
}
