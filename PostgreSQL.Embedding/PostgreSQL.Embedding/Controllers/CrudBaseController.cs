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
    public class CrudBaseController<T>: ControllerBase where T : BaseEntity, new()
    {
        private readonly CrudBaseService<T> _crudBaseService;
        public CrudBaseController(CrudBaseService<T> crudBaseService)
        {
            _crudBaseService = crudBaseService;
        }

        [HttpGet("{id}")]
        public virtual async Task<JsonResult> SelectById(long id)
        {
            var instance = await _crudBaseService.GetById(id);
            return ApiResult.Success(instance, "操作成功");
        }

        [HttpPost]
        public virtual async Task<JsonResult> Create(T entity)
        {
            var instance = await _crudBaseService.Create(entity);
            return ApiResult.Success(instance, "操作成功");
        }

        [HttpPut]
        public virtual async Task<JsonResult> Update(T entity)
        {
            await _crudBaseService.Update(entity);
            return ApiResult.Success(new { }, "操作成功");
        }

        [HttpDelete]
        public virtual async Task<JsonResult> Delete(string ids)
        {
            await _crudBaseService.Delete(ids);
            return ApiResult.Success(new { }, "操作成功");
        }

        [HttpGet("paginate")]
        public virtual async Task<JsonResult> GetByPage(int pageSize, int pageIndex)
        {
            var result = await _crudBaseService.GetPageList(pageSize, pageIndex);
            return ApiResult.Success(result, "");
        }
    }
}
