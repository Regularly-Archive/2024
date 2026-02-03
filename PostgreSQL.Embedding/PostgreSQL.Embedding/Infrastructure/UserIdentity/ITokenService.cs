using PostgreSQL.Embedding.Domain.Entities;

namespace PostgreSQL.Embedding.Infrastructure.UserIdentity
{
    /// <summary>
    /// Token 服务接口
    /// </summary>
    public interface ITokenService
    {
        /// <summary>
        /// 为用户生成 JWT Token
        /// </summary>
        string GenerateToken(SystemUser user);

        /// <summary>
        /// 从 ClaimsPrincipal 获取用户名
        /// </summary>
        string? GetUserNameFromPrincipal(System.Security.Claims.ClaimsPrincipal principal);
    }
}
