using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using System.Text.Json;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CrudBaseController<T, TQueryableFilter> : ControllerBase where T : BaseEntity, new() where TQueryableFilter : class, IQueryableFilter<T>
    {
        protected readonly CrudBaseService<T> _crudBaseService;
        public CrudBaseController(CrudBaseService<T> crudBaseService)
        {
            _crudBaseService = crudBaseService;
        }

        [HttpGet("{id}")]
        public virtual async Task<JsonResult> SelectByIdAsync(long id)
        {
            var instance = await _crudBaseService.GetByIdAsync(id);
            return ApiResult.Success(instance, "操作成功");
        }

        [HttpPost]
        public virtual async Task<JsonResult> CreateAsync(T entity)
        {
            var instance = await _crudBaseService.CreateAsync(entity);
            return ApiResult.Success(instance, "操作成功");
        }

        [HttpPut]
        public virtual async Task<JsonResult> UpdateAsync(T entity)
        {
            await _crudBaseService.UpdateAsync(entity);
            return ApiResult.Success(new { }, "操作成功");
        }

        [HttpDelete]
        public virtual async Task<JsonResult> DeleteAsync(string ids)
        {
            await _crudBaseService.DeleteAsync(ids);
            return ApiResult.Success(new { }, "操作成功");
        }

        [HttpGet("paginate")]
        public virtual async Task<JsonResult> GetByPageAsync([FromQuery]QueryParameter<T,TQueryableFilter> queryParameter)
        {
            var result = await _crudBaseService.GetPagedListAsync(queryParameter);
            return ApiResult.Success(result, "操作成功");
        }

        [HttpGet("list")]
        public virtual async Task<JsonResult> FindListAsync([FromQuery]TQueryableFilter filter = null)
        {
            var results = await _crudBaseService.GetListAsync(filter);
            return ApiResult.Success(results, "操作成功");
        }
    }
}
