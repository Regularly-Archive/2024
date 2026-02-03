using Mapster;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.User;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using System.Security.Claims;

namespace PostgreSQL.Embedding.Infrastructure.UserIdentity
{
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IRepository<SystemUser> _systemUserRepository;

        public CurrentUserService(
            IHttpContextAccessor httpContextAccessor,
            IRepository<SystemUser> systemUserRepository)
        {
            _httpContextAccessor = httpContextAccessor;
            _systemUserRepository = systemUserRepository;
        }

        public async Task<SystemUser?> GetCurrentUserAsync()
        {
            var userName = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            if (string.IsNullOrEmpty(userName))
            {
                return null;
            }

            return await _systemUserRepository.FindAsync(x => x.UserName == userName);
        }

        public async Task<UserIdentityInfo?> GetCurrentIdentityAsync()
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return null;
            }

            return new UserIdentityInfo
            {
                Id = user.Id,
                UserName = user.UserName,
                NickName = user.NickName,
                Avatar = user.Avatar,
                Intro = user.Intro,
                Gender = user.Gender,
                Role = user.Role ?? "User"
            };
        }

        public async Task<SystemUser?> GetByIdAsync(long userId)
        {
            return await _systemUserRepository.GetAsync(userId);
        }

        public async Task UpdateProfileAsync(UpdateProfileRequest request)
        {
            var systemUser = await _systemUserRepository.GetAsync(request.Id);
            if (systemUser == null)
            {
                throw new ArgumentException("指定用户不存在");
            }

            request.Adapt(systemUser);
            await _systemUserRepository.UpdateAsync(systemUser);
        }

        public bool IsAdmin()
        {
            var userName = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            if (userName == "admin")
            {
                return true;
            }

            var user = _httpContextAccessor.HttpContext?.User;
            if (user != null)
            {
                var roles = user.Claims
                    .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
                    .Select(c => c.Value)
                    .ToList();

                return roles.Contains("SA") || roles.Contains("Admin");
            }

            return false;
        }
    }
}
