using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.WebApi;
using PostgreSQL.Embedding.Domain.Models.WebApi.QuerableFilters;
using PostgreSQL.Embedding.Infrastructure.DataAccess;

namespace PostgreSQL.Embedding.Application.Controllers.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MCPServerController : CrudBaseController<MCPServer, MCPServerQueryableFilter>
    {
        private McpConnectionFactory _mcpCientFactory;
        public MCPServerController(CrudBaseService<MCPServer> crudBaseService, McpConnectionFactory mcpClientFactory) 
            : base(crudBaseService)
        {
            _mcpCientFactory = mcpClientFactory;
        }

        public override async Task<JsonResult> CreateAsync(MCPServer entity)
        {
            var repository = _crudBaseService.Repository;
            var exists = await repository.ExistsAsync(x => x.AppId == entity.AppId && x.Name == entity.Name);
            if (exists) throw new Exception($"当前应用已存在名为 '{entity.Name}' 的 MCP 服务器");
            
            return await base.CreateAsync(entity);
        }

        [HttpPost("{id}/test")]
        public async Task<JsonResult> TestAsync(long id)
        {
            var repository = _crudBaseService.Repository;
            var mcpServer = await repository.GetAsync(id);
            if (mcpServer == null) throw new Exception("指定的 MCP 服务器不存在");

            var mcpConnection = _mcpCientFactory.GetOrCreate(mcpServer);
            var tools = mcpConnection.GetTools();

            return ApiResult.Success<object>(null);
        }
    }
}
