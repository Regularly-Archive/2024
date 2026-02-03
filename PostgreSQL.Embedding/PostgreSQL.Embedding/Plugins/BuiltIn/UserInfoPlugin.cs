using Mapster;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models.User;
using PostgreSQL.Embedding.Infrastructure.UserIdentity;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
    [KernelPlugin(Description = "根据用户 ID 查询用户基本信息（昵称、头像等），返回格式化的用户资料。", Version = "1.2")]
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
            var currentUserService = serviceScope.ServiceProvider.GetRequiredService<ICurrentUserService>();

            var user = await currentUserService.GetByIdAsync(userId);
            if (user == null)
            {
                return "用户不存在";
            }

            var userInfoDto = new UserInfo
            {
                Id = user.Id.ToString(),
                UserName = user.UserName,
                NickName = user.NickName,
                Avatar = user.Avatar,
                Gender = user.Gender,
                Role = string.IsNullOrEmpty(user.Role)
                    ? new List<string>()
                    : new List<string> { user.Role }
            };

            return JsonConvert.SerializeObject(userInfoDto, Formatting.Indented);
        }
    }
}
