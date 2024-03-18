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
        public async Task<JsonResult> SelectById(long id)
        {
            var instance = await _crudBaseService.GetById(id);
            return ApiResult.Success(instance);
        }

        [HttpPut]
        public async Task<JsonResult> Update(T entity)
        {
            await _crudBaseService.Update(entity);
            return ApiResult.Success(new { },"更新成功");
        }

        [HttpDelete]
        public async Task<JsonResult> Delete(string ids)
        {
            await _crudBaseService.Delete(ids);
            return ApiResult.Success(new { }, "删除成功");
        }

        [HttpGet("paginate")]
        public async Task<JsonResult> GetByPage(int pageSize, int pageIndex)
        {
            var result = await _crudBaseService.GetPageList(pageSize, pageIndex);
            return ApiResult.Success(result, "");
        }
    }
}
