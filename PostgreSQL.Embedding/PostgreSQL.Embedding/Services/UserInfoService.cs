using Mapster;
using Masuit.Tools.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PostgreSQL.Embedding.Common.Models.User;
using PostgreSQL.Embedding.Common.Settings;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PostgreSQL.Embedding.Services
{
    public interface IUserInfoService
    {
        Task<UserInfo> GetCurrentUserAsync();

        Task<LoginResult> LoginAsync(Common.Models.User.LoginRequest request);

        Task RegisterAsync(Common.Models.User.RegisterRequest request);

        Task<SystemUser> GetUserByIdAsync(long userId);

        Task ChangePassword(ChangePasswordRequest request);

        Task UpdateProfile(UpdateProfileRequest request);
    }

    public class UserInfoService : IUserInfoService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IRepository<SystemUser> _systemUserRepository;
        private readonly IOptions<JwtSetting> _jwtSettingOptions;
        private const string Default_AES_Key = "V2lraXRBZG1pbg==";
        public UserInfoService(
            IHttpContextAccessor httpContextAccessor,
            IRepository<SystemUser> systemUserRepository,
            IOptions<JwtSetting> jwtSettingOptions
        )
        {
            _httpContextAccessor = httpContextAccessor;
            _systemUserRepository = systemUserRepository;
            _jwtSettingOptions = jwtSettingOptions;
        }

        public async Task<UserInfo> GetCurrentUserAsync()
        {
            var userName = _httpContextAccessor.HttpContext?.User.Identity?.Name;
            if (userName == null) return null;

            var userInfo = await _systemUserRepository.SingleOrDefaultAsync(x => x.UserName == userName);
            if (userName == null) return null;

            return userInfo.Adapt<UserInfo>();
        }

        public async Task<LoginResult> LoginAsync(Common.Models.User.LoginRequest request)
        {
            var encrypted = request.Password.AESEncrypt(Default_AES_Key);
            var userInfo = await _systemUserRepository.SingleOrDefaultAsync(x => x.UserName == request.UserName && x.Password == encrypted);
            if (userInfo == null) throw new ArgumentException("用户名或密码不正确");

            var token = GenerateJwtToken(userInfo);
            return new LoginResult
            {
                Token = token,
                UserInfo = new Common.Models.User.UserInfo()
                {
                    Id = userInfo.Id.ToString(),
                    UserName = userInfo.UserName,
                    NickName = userInfo.NickName,
                    Avatar = userInfo.Avatar,
                    Gender = userInfo.Gender,
                    Role = new List<string> { "SA" },
                }
            };
        }

        public async Task RegisterAsync(Common.Models.User.RegisterRequest request)
        {
            var encrypted = request.Password.AESEncrypt(Default_AES_Key);
            var userInfo = await _systemUserRepository.SingleOrDefaultAsync(x => x.UserName == request.UserName);
            if (userInfo != null) throw new ArgumentException("该用户已存在");

            var newUser = new SystemUser
            {
                UserName = request.UserName,
                Password = encrypted,
            };
            await _systemUserRepository.AddAsync(newUser);
        }

        public async Task<SystemUser> GetUserByIdAsync(long userId)
        {
            var systemUser = await _systemUserRepository.GetAsync(userId);
            if (systemUser == null) throw new ArgumentException("指定用户不存在");

            return systemUser;
        }

        public async Task UpdateProfile(UpdateProfileRequest request)
        {
            var systemUser = await _systemUserRepository.GetAsync(request.Id);
            if (systemUser == null) throw new ArgumentException("指定用户不存在"); ;

            request.Adapt(systemUser);
            await _systemUserRepository.UpdateAsync(systemUser);
        }

        public async Task ChangePassword(ChangePasswordRequest request)
        {
            var currentUserName = (await GetCurrentUserAsync()).UserName;
            if (currentUserName != request.UserName)
                throw new ArgumentException("不允许修改他人密码");

            var currentUser = await _systemUserRepository.SingleOrDefaultAsync(x => x.UserName == request.UserName);
            if (currentUser == null) throw new ArgumentException("指定用户不存在");

            if (request.OldPassword == request.NewPassword)
                throw new ArgumentException("新/旧密码不能相同");

            var encryptedPassword = request.NewPassword.AESEncrypt(Default_AES_Key);
            currentUser.Password = encryptedPassword;
            await _systemUserRepository.UpdateAsync(currentUser);
        }

        private string GenerateJwtToken(SystemUser systemUser)
        {
            var jwtSetting = _jwtSettingOptions.Value;
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, systemUser.UserName),
                new Claim(ClaimTypes.NameIdentifier, systemUser.Id.ToString()),
                new Claim(ClaimTypes.Role, "")
            };

            var secretKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSetting.Secret));
            var credentials = new SigningCredentials(secretKey, SecurityAlgorithms.HmacSha256);
            var jwtToken = new JwtSecurityToken(
                issuer: jwtSetting.Issuer,
                audience: jwtSetting.Audience,
                claims: claims,
                expires: DateTime.Now.Add(jwtSetting.Expires),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(jwtToken);
        }
    }
}
