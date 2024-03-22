using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LlmModelController : CrudBaseController<LlmModel>
    {
        private readonly IRepository<LlmModel> _llmModelRepository;
        public LlmModelController(CrudBaseService<LlmModel> crudBaseService, IRepository<LlmModel> llmModelRepository) : base(crudBaseService)
        {
            _llmModelRepository = llmModelRepository;
        }

        [HttpGet("list/{modeType}")]
        public async Task<JsonResult> GetModelDropdownList(int modeType)
        {
            var modes = await _llmModelRepository.FindAsync(x => x.ModelType == modeType);
            return ApiResult.Success(modes);
        }
    }
}
