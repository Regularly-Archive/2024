using Masuit.Tools.Security;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.User;
using PostgreSQL.Embedding.Infrastructure.DataAccess;

namespace PostgreSQL.Embedding.Infrastructure.UserIdentity
{
    public class AuthenticationService : IAuthenticationService
    {
        private const string Default_AES_Key = "V2lraXRBZG1pbg==";
        private readonly IRepository<SystemUser> _systemUserRepository;
        private readonly ITokenService _tokenService;

        public AuthenticationService(
            IRepository<SystemUser> systemUserRepository,
            ITokenService tokenService)
        {
            _systemUserRepository = systemUserRepository;
            _tokenService = tokenService;
        }

        public async Task<LoginResult> LoginAsync(LoginRequest request)
        {
            var encrypted = request.Password.AESEncrypt(Default_AES_Key);
            var user = await _systemUserRepository.FindAsync(x =>
                x.UserName == request.UserName && x.Password == encrypted);

            if (user == null)
            {
                throw new ArgumentException("用户名或密码不正确");
            }

            var token = _tokenService.GenerateToken(user);
            return new LoginResult
            {
                Token = token,
                UserInfo = new UserInfo
                {
                    Id = user.Id.ToString(),
                    UserName = user.UserName,
                    NickName = user.NickName,
                    Avatar = user.Avatar,
                    Gender = user.Gender,
                    Role = new List<string> { user.Role ?? "User" }
                }
            };
        }

        public async Task RegisterAsync(RegisterRequest request)
        {
            // 检查用户是否已存在
            var existingUser = await _systemUserRepository.FindAsync(x =>
                x.UserName == request.UserName);

            if (existingUser != null)
            {
                throw new ArgumentException("该用户已存在");
            }

            var encrypted = request.Password.AESEncrypt(Default_AES_Key);
            var newUser = new SystemUser
            {
                UserName = request.UserName,
                Password = encrypted,
                NickName = request.UserName,
                Role = "User"  // 默认普通用户
            };

            await _systemUserRepository.AddAsync(newUser);
        }

        public async Task ChangePasswordAsync(ChangePasswordRequest request)
        {
            var currentUser = await _systemUserRepository.FindAsync(x =>
                x.UserName == request.UserName);

            if (currentUser == null)
            {
                throw new ArgumentException("指定用户不存在");
            }

            // 验证旧密码
            var oldEncrypted = request.OldPassword.AESEncrypt(Default_AES_Key);
            if (currentUser.Password != oldEncrypted)
            {
                throw new ArgumentException("旧密码不正确");
            }

            // 检查新旧密码是否相同
            var newEncrypted = request.NewPassword.AESEncrypt(Default_AES_Key);
            if (currentUser.Password == newEncrypted)
            {
                throw new ArgumentException("新密码不能与旧密码相同");
            }

            currentUser.Password = newEncrypted;
            await _systemUserRepository.UpdateAsync(currentUser);
        }
    }
}
