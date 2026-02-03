using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Domain.Entities;
using SqlSugar;

namespace PostgreSQL.Embedding.Application.Controllers.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class CodeFirstController : ControllerBase
    {
        private readonly ISqlSugarClient _sqlSugarClient;
        public CodeFirstController(ISqlSugarClient sqlSugarClient)
        {
            _sqlSugarClient = sqlSugarClient;
        }

        /// <summary>
        /// 全局初始化 - 自动扫描所有继承 BaseEntity 的实体
        /// </summary>
        [HttpGet("init")]
        public async Task<IActionResult> InitAll()
        {
            _sqlSugarClient.DbMaintenance.CreateDatabase();

            // 自动扫描所有继承 BaseEntity 的实体类型
            var entityTypes = typeof(BaseEntity).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(BaseEntity)))
                .ToArray();

            foreach (var entityType in entityTypes)
            {
                _sqlSugarClient.CodeFirst.InitTables(entityType);
            }

            await _sqlSugarClient.Ado.ExecuteCommandAsync($"CREATE EXTENSION IF NOT EXISTS vector;");

            return Ok(new
            {
                Message = "初始化成功",
                Tables = entityTypes.Select(t => t.Name)
            });
        }
    }
}
