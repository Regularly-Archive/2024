using System.Security.Claims;

namespace PostgreSQL.Embedding.Services
{
    public interface IUserInfoService
    {
        ClaimsPrincipal GetCurrentUser();
    }

    public class UserInfoService : IUserInfoService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        public UserInfoService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }


        public ClaimsPrincipal GetCurrentUser()
        {
            return _httpContextAccessor.HttpContext?.User;
        }
    }
}
