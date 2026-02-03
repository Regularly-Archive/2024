using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Domain.Models.User;
using PostgreSQL.Embedding.Domain.Models.WebApi;
using PostgreSQL.Embedding.Infrastructure.UserIdentity;

namespace PostgreSQL.Embedding.Application.Controllers.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class AccountController : ControllerBase
    {
        private readonly IAuthenticationService _authenticationService;
        private readonly ICurrentUserService _currentUserService;

        public AccountController(
            IAuthenticationService authenticationService,
            ICurrentUserService currentUserService)
        {
            _authenticationService = authenticationService;
            _currentUserService = currentUserService;
        }

        [HttpPost("login")]
        public async Task<JsonResult> Login([FromBody] LoginRequest loginRequest)
        {
            var loginResult = await _authenticationService.LoginAsync(loginRequest);
            return ApiResult.Success(loginResult);
        }

        [HttpPost("register")]
        public async Task<JsonResult> Register([FromBody] RegisterRequest registerRequest)
        {
            await _authenticationService.RegisterAsync(registerRequest);
            return ApiResult.Success(new { }, "注册成功");
        }

        [HttpGet("{id}")]
        public virtual async Task<JsonResult> SelectById(long id)
        {
            var user = await _currentUserService.GetByIdAsync(id);
            if (user == null)
            {
                throw new ArgumentException("用户不存在");
            }

            var userInfo = new UserInfo
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

            return ApiResult.Success(userInfo, "操作成功");
        }

        [HttpPut]
        public virtual async Task<JsonResult> Update([FromBody] UpdateProfileRequest request)
        {
            await _currentUserService.UpdateProfileAsync(request);
            return ApiResult.Success(new { }, "操作成功");
        }

        [HttpPost("ChangePassword")]
        public async Task<JsonResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            await _authenticationService.ChangePasswordAsync(request);
            return ApiResult.Success(new { }, "操作成功");
        }
    }
}
