using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LlmAppController : CrudBaseController<LlmApp>
    {
        private readonly IRepository<LlmAppKnowledge> _appKnowledgeRepository;
        private readonly IRepository<KnowledgeBase> _knowledgeBaseRepository;
        private readonly IRepository<LlmApp> _llmAppRepository;
        public LlmAppController(
            CrudBaseService<LlmApp> crudBaseService,
            IRepository<LlmAppKnowledge> appKnowledgeRepository,
            IRepository<KnowledgeBase> knowledgeBaseRepository,
            IRepository<LlmApp> llmAppRepository
            ) : base(crudBaseService)
        {
            _appKnowledgeRepository = appKnowledgeRepository;
            _knowledgeBaseRepository = knowledgeBaseRepository;
            _llmAppRepository = llmAppRepository;
        }

        [HttpGet("{id}/knowledges")]
        public async Task<JsonResult> GetKnowledgeBasesByApp(long id)
        {
            var appKnowledges = await _appKnowledgeRepository.FindAsync(x => x.AppId == id);
            var knowledgeIds = appKnowledges.Select(x => x.KnowledgeBaseId).ToList();
            var knowledgeBases = await _knowledgeBaseRepository.FindAsync(x => knowledgeIds.Contains(x.Id));
            return ApiResult.Success(knowledgeBases);
        }

        [HttpPut("{id}/knowledges")]
        public async Task<JsonResult> UpdateAppKnowledges(long id, [FromBody] List<KnowledgeBase> knowledges)
        {
            await _appKnowledgeRepository.DeleteAsync(x => x.AppId == id);
            if (knowledges != null && knowledges.Any())
            {
                var appKnowledges = knowledges.Select(x => new LlmAppKnowledge() { AppId = id, KnowledgeBaseId = x.Id }).ToArray();
                await _appKnowledgeRepository.AddAsync(appKnowledges);
            }
            return ApiResult.Success<object>(null);
        }

        [HttpDelete("{appId}/knowledges/{knowledgeBaseId}")]
        public async Task<JsonResult> DeleteAppKnowledges(long appId, long knowledgeBaseId)
        {
            await _appKnowledgeRepository.DeleteAsync(x => x.AppId == appId && x.KnowledgeBaseId == knowledgeBaseId);
            return ApiResult.Success<object>(null);
        }


        [HttpGet("{id}")]
        public override async Task<JsonResult> SelectById(long id)
        {
            var app = await _llmAppRepository.GetAsync(id);
            var appKnowledges = await _appKnowledgeRepository.FindAsync(x => x.AppId == id);
            var knowledgeIds = appKnowledges.Select(x => x.KnowledgeBaseId).ToList();
            app.KnowledgeBaseIds = knowledgeIds;
            return ApiResult.Success(app);
        }
    }
}
