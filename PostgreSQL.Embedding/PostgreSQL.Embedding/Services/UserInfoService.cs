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
        ClaimsPrincipal GetCurrentUser();

        Task<LoginResult> LoginAsync(Common.Models.User.LoginRequest request);

        Task RegisterAsync(Common.Models.User.RegisterRequest request);


    }

    public class UserInfoService : IUserInfoService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IRepository<SystemUser> _systemUserRepository;
        private readonly IOptions<JwtSetting> _jwtSettingOptions;
        private const string Default_AES_Key = "V2lraXRBZG1pbg==";
        public UserInfoService(IHttpContextAccessor httpContextAccessor, IRepository<SystemUser> systemUserRepository, IOptions<JwtSetting> jwtSettingOptions)
        {
            _httpContextAccessor = httpContextAccessor;
            _systemUserRepository = systemUserRepository;
            _jwtSettingOptions = jwtSettingOptions;
        }


        public ClaimsPrincipal GetCurrentUser()
        {
            return _httpContextAccessor.HttpContext?.User;
        }

        public async Task<LoginResult> LoginAsync(Common.Models.User.LoginRequest request)
        {
            var encrypted = request.Password.AESEncrypt(Default_AES_Key);
            var userInfo = await _systemUserRepository.SingleOrDefaultAsync(x => x.UserName == request.UserName && x.Password == encrypted);
            if (userInfo == null) throw new ArgumentException("用户名或密码不正确");

            var token = GenerateJwtToken(userInfo);
            return new LoginResult
            {
                AccessToken = token,
                UserInfo = new UserInfo()
                {
                    Id = userInfo.Id.ToString(),
                    UserName = userInfo.UserName,
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
