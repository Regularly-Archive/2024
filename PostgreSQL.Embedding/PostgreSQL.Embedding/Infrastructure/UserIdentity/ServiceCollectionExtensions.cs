using PostgreSQL.Embedding.Infrastructure.UserIdentity;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class UserIdentityServiceCollectionExtensions
    {
        /// <summary>
        /// 添加用户身份认证服务
        /// </summary>
        public static IServiceCollection AddUserIdentityServices(this IServiceCollection services)
        {
            services.AddScoped<ITokenService, TokenService>();
            services.AddScoped<ICurrentUserService, CurrentUserService>();
            services.AddScoped<IAuthenticationService, AuthenticationService>();

            return services;
        }
    }
}
