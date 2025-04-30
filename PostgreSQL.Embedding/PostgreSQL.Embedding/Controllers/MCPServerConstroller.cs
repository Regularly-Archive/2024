using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MCPServerController : CrudBaseController<MCPServer, EmptyQueryFilter<MCPServer>>
    {
        public MCPServerController(CrudBaseService<MCPServer> crudBaseService) 
            : base(crudBaseService)
        {

        }
    }
}
