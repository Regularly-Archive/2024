using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Metadata;
using PostgreSQL.Embedding.DataAccess.Entities;
using SqlSugar;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CodeFirstController : ControllerBase
    {
        private readonly ISqlSugarClient _sqlSugarClient;
        public CodeFirstController(ISqlSugarClient sqlSugarClient)
        {
            _sqlSugarClient = sqlSugarClient;
        }

        /// <summary>
        /// 全局初始化
        /// </summary>
        /// <returns></returns>
        [HttpGet("init")]
        public IActionResult InitAll()
        {
            _sqlSugarClient.DbMaintenance.CreateDatabase();
            _sqlSugarClient.CodeFirst.InitTables(typeof(LlmApp));
            _sqlSugarClient.CodeFirst.InitTables(typeof(LlmModel));
            _sqlSugarClient.CodeFirst.InitTables(typeof(KnowledgeBase));
            _sqlSugarClient.CodeFirst.InitTables(typeof(LlmAppKnowledge));
            _sqlSugarClient.CodeFirst.InitTables(typeof(DocumentImportRecord));
            _sqlSugarClient.CodeFirst.InitTables(typeof(ChatMessage));
            _sqlSugarClient.Ado.ExecuteCommandAsync($"CREATE EXTENSION IF NOT EXISTS vector;");
            return Ok();
        }
    }
}
