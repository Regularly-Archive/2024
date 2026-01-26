using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.WebApi.QuerableFilters;
using PostgreSQL.Embedding.Infrastructure.DataAccess;

namespace PostgreSQL.Embedding.Application.Controllers.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MCPServerController : CrudBaseController<MCPServer, MCPServerQueryableFilter>
    {
        public MCPServerController(CrudBaseService<MCPServer> crudBaseService) 
            : base(crudBaseService)
        {

        }

        public override async Task<JsonResult> CreateAsync(MCPServer entity)
        {
            var repository = _crudBaseService.Repository;
            var exists = await repository.ExistsAsync(x => x.AppId == entity.AppId && x.Name == entity.Name);
            if (exists) throw new Exception($"当前应用已存在名为 '{entity.Name}' 的 MCP 服务器");
            
            return await base.CreateAsync(entity);
        }
    }
}
