using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Models.User;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.Services;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class AccountController : ControllerBase
    {
        private readonly IUserInfoService _useInfoService;
        public AccountController(IUserInfoService userInfoService)
        {
            _useInfoService = userInfoService;
        }

        [HttpPost("login")]
        public async Task<JsonResult> Login([FromBody] LoginRequest loginRequest)
        {
            var loginResult = await _useInfoService.LoginAsync(loginRequest);
            return ApiResult.Success(loginResult);
        }

        [HttpPost("register")]
        public async Task<JsonResult> Register([FromBody] RegisterRequest registerRequest)
        {
            await _useInfoService.RegisterAsync(registerRequest);
            return ApiResult.Success(new { }, "注册成功");
        }
    }
}   
