using PostgreSQL.Embedding.Domain.Models.User;

namespace PostgreSQL.Embedding.Infrastructure.UserIdentity
{
    /// <summary>
    /// 认证服务接口
    /// </summary>
    public interface IAuthenticationService
    {
        /// <summary>
        /// 用户登录
        /// </summary>
        Task<LoginResult> LoginAsync(LoginRequest request);

        /// <summary>
        /// 用户注册
        /// </summary>
        Task RegisterAsync(RegisterRequest request);

        /// <summary>
        /// 修改密码
        /// </summary>
        Task ChangePasswordAsync(ChangePasswordRequest request);
    }
}
