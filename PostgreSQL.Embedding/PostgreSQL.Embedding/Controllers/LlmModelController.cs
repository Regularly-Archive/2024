using Masuit.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.Common.Models.WebApi.QuerableFilters;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LlmModelController : CrudBaseController<LlmModel,LlmModelQueryableFilter>
    {
        private readonly IRepository<LlmModel> _llmModelRepository;
        public LlmModelController(CrudBaseService<LlmModel> crudBaseService, IRepository<LlmModel> llmModelRepository) : base(crudBaseService)
        {
            _llmModelRepository = llmModelRepository;
        }

        [HttpPatch("{id}/SetAsDefault")]

        public async Task<JsonResult> SetAsDefault(long id)
        {
            var llmModel = await _crudBaseService.GetByIdAsync(id);
            if (llmModel == null) return ApiResult.Failure("请检查当前模型信息是否正确");

            var defaultLlmModel = await _crudBaseService.Repository.FindAsync(x => x.IsDefaultModel == true && x.ModelType == llmModel.ModelType);
            if (defaultLlmModel != null)
            {
                defaultLlmModel.IsDefaultModel = false;
                await _crudBaseService.Repository.UpdateAsync(defaultLlmModel);
            }

            llmModel.IsDefaultModel = true;
            await _crudBaseService.Repository.UpdateAsync(llmModel);

            return ApiResult.Success(llmModel);
        }
    }
}
