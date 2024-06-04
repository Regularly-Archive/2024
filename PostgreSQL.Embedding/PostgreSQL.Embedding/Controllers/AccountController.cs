using DocumentFormat.OpenXml.Office2010.Excel;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Models.User;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.DataAccess;
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

        [HttpGet("{id}")]
        public virtual async Task<JsonResult> SelectById(long id)
        {
            var userInfo = await _useInfoService.GetUserByIdAsync(id);
            var queryDTO = userInfo.Adapt<UserInfo>();
            return ApiResult.Success(queryDTO, "操作成功");
        }

        [HttpPut]
        public virtual async Task<JsonResult> Update([FromBody] UpdateProfileRequest request)
        {
            await _useInfoService.UpdateProfile(request);
            return ApiResult.Success(new { }, "操作成功");
        }

        [HttpPost("ChangePassword")]
        public async Task<JsonResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            await _useInfoService.ChangePassword(request);
            return ApiResult.Success(new { }, "操作成功");
        }
    }
}   
