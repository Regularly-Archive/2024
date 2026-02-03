using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PostgreSQL.Embedding.Common.Settings;
using PostgreSQL.Embedding.Domain.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PostgreSQL.Embedding.Infrastructure.UserIdentity
{
    public class TokenService : ITokenService
    {
        private readonly JwtSetting _jwtSetting;

        public TokenService(IOptions<JwtSetting> jwtSettingOptions)
        {
            _jwtSetting = jwtSettingOptions.Value;
        }

        public string GenerateToken(SystemUser user)
        {
            var secretKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSetting.Secret));
            var credentials = new SigningCredentials(secretKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Role, user.Role ?? "User"),
                new Claim("nickName", user.NickName ?? ""),
                new Claim("avatar", user.Avatar ?? "")
            };

            var jwtToken = new JwtSecurityToken(
                issuer: _jwtSetting.Issuer,
                audience: _jwtSetting.Audience,
                claims: claims,
                expires: DateTime.Now.Add(_jwtSetting.Expires),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(jwtToken);
        }

        public string? GetUserNameFromPrincipal(ClaimsPrincipal principal)
        {
            return principal.Identity?.Name;
        }
    }
}
