
using Mapster;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models.User;
using PostgreSQL.Embedding.Infrastructure;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
    [KernelPlugin(Description = "根据用户 ID 查询用户基本信息（昵称、头像等），返回 JSON 格式的用户资料。", Version = "1.1")]
    public class UserInfoPlugin : BasePlugin
    {
        private readonly IServiceProvider _serviceProvider;
        public UserInfoPlugin(IServiceProvider serviceProvider) : base(serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [KernelFunction]
        [Description("根据用户 ID 查询用户信息，返回包含昵称、头像等字段的 JSON 对象")]
        public async Task<string> GetUserInfoAsync([Description("要查询的用户 ID")] long userId)
        {
            using var serviceScope = _serviceProvider.CreateScope();
            var userInfoService = serviceScope.ServiceProvider.GetRequiredService<IUserInfoService>();

            var userInfo = await userInfoService.GetUserByIdAsync(userId);
            var userInfoDto = userInfo.Adapt<UserInfo>();
            return JsonConvert.SerializeObject(userInfoDto);
        }
    }
}
