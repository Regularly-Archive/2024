
using Mapster;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Models.User;
using PostgreSQL.Embedding.Services;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "用户信息插件")]
    public class UserInfoPlugin
    {
        private readonly IServiceProvider _serviceProvider;
        public UserInfoPlugin(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [KernelFunction]
        [Description("获取指定用户信息")]
        public async Task<string> GetUserInfoAsync([Description("用户Id")] long userId)
        {
            using var serviceScope = _serviceProvider.CreateScope();
            var userInfoService = serviceScope.ServiceProvider.GetRequiredService<IUserInfoService>();

            var userInfo = await userInfoService.GetUserByIdAsync(userId);
            var userInfoDto = userInfo.Adapt<UserInfo>();
            return JsonConvert.SerializeObject(userInfoDto);
        }
    }
}
