using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.Common.Models.WebApi.QuerableFilters;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LlmPluginController : ControllerBase
    {
        private readonly ILlmPluginService _llmPluginService;
        public LlmPluginController(ILlmPluginService llmPluginService)
        {
            _llmPluginService = llmPluginService;
        }

        [HttpGet("paginate")]
        public async Task<JsonResult> GetByPage([FromQuery]QueryParameter<LlmPlugin, PluginQueryableFilter> queryParameter)
        {
            var result = await _llmPluginService.GetPagedPluginListAsync(queryParameter);
            return ApiResult.Success(result, "操作成功");
        }

        [HttpGet("list")]
        public async Task<JsonResult> FindList(PluginQueryableFilter filter)
        {
            var results = await _llmPluginService.GetPluginListAsync(filter);
            return ApiResult.Success(results, "操作成功");
        }

        [HttpGet("{id}")]
        public async Task<JsonResult> SelectById(long id)
        {
            var result = await _llmPluginService.GetPluginByIdAsync(id);
            return ApiResult.Success(result, "操作成功");
        }

        [HttpPut("{id}/status/{status}")]
        public async Task<JsonResult> ChangePluginStatus(long id, bool status)
        {
            await _llmPluginService.ChangePluginStatusAsync(id, status);
            return ApiResult.Success<object>(null, "操作成功");
        }
    }
}
