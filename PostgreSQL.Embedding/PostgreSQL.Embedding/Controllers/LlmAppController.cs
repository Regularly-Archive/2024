using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LlmAppController : CrudBaseController<LlmApp>
    {
        public LlmAppController(CrudBaseService<LlmApp> crudBaseService) : base(crudBaseService)
        {

        }
    }
}
