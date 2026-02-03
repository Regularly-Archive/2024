using System.Security.Claims;
using PostgreSQL.Embedding.Common;

namespace PostgreSQL.Embedding.Infrastructure.DataAccess
{
    /// <summary>
    /// 数据隔离服务实现
    /// </summary>
    public class DataIsolationService : IDataIsolationService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public DataIsolationService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string? GetCurrentUserId()
        {
            // 优先从 Claims 获取
            var userName = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            if (!string.IsNullOrEmpty(userName))
            {
                return userName;
            }

            return null;
        }

        public bool IsAdmin()
        {
            // 检查是否是 admin 用户
            var userName = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            if (userName == Constants.Admin)
            {
                return true;
            }

            // 检查 Role 中是否包含 SA
            var user = _httpContextAccessor.HttpContext?.User;
            if (user != null)
            {
                var roles = user.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
                    .Select(c => c.Value)
                    .ToList();

                if (roles.Contains("SA") || roles.Contains("Admin"))
                {
                    return true;
                }
            }

            return false;
        }

        public bool ShouldIsolate(Type entityType)
        {
            // 如果是管理员，不进行数据隔离
            if (IsAdmin())
            {
                return false;
            }

            // 检查 entityType 是否标记了 DataIsolationAttribute
            return Attribute.IsDefined(entityType, typeof(DataIsolationAttribute));
        }
    }
}
