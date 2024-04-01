using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class KnowledgeBaseController : CrudBaseController<KnowledgeBase>
    {
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IKnowledgeBaseService _knowledgeBaseService;
        public KnowledgeBaseController(
            IWebHostEnvironment hostingEnvironment,
            IKnowledgeBaseService knowledgeBaseService,
            CrudBaseService<KnowledgeBase> crudBaseService) : base(crudBaseService)
        {
            _webHostEnvironment = hostingEnvironment;
            _knowledgeBaseService = knowledgeBaseService;
        }

        [HttpGet("{knowledgeBaseId}/search")]
        public async Task<JsonResult> KnowledgeSearch(long knowledgeBaseId, [FromQuery] string question, [FromQuery] double minRelevance, [FromQuery] int limit)
        {
            var searchResults = await _knowledgeBaseService.SearchAsync(knowledgeBaseId, question, minRelevance, limit);
            return ApiResult.Success(searchResults);
        }

        [HttpGet("{knowledgeBaseId}/ask")]
        public async Task<JsonResult> KnowledgeBaseAsk(long knowledgeBaseId, [FromQuery] string question, [FromQuery] double minRelevance)
        {
            var askResults = await _knowledgeBaseService.AskAsync(knowledgeBaseId, question, minRelevance);
            return ApiResult.Success(askResults);
        }

        [HttpPost("{knowledgeBaseId}/embedding/files")]
        public async Task<JsonResult> CreateEmbeddingFromFile(long knowledgeBaseId, List<IFormFile> files)
        {
            var embeddingTaskId = Guid.NewGuid().ToString("N");
            var embedingTaskFolder = Path.Combine(_webHostEnvironment.ContentRootPath, "Upload", embeddingTaskId);
            if (!Directory.Exists(embedingTaskFolder))
                Directory.CreateDirectory(embedingTaskFolder);

            var uploadedFiles = new List<string>();

            if (files != null && files.Any())
            {
                foreach (var file in files)
                {
                    if (file.Length <= 0) continue;

                    var filePath = Path.Combine(embedingTaskFolder, file.FileName);
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        file.CopyTo(fileStream);
                        uploadedFiles.Add(filePath);
                    }
                }
            }

            if (uploadedFiles.Any())
            {
                await _knowledgeBaseService.ImportKnowledgeFromFiles(embeddingTaskId, knowledgeBaseId, uploadedFiles);

            }

            return ApiResult.Success(new ImportingTaskResult()
            {
                KnowledgeBaseId = knowledgeBaseId.ToString(),
                ImportingTaskId = embeddingTaskId
            });
        }

        [HttpPost("{knowledgeBaseId}/embedding/url")]
        public JsonResult CreateEmbeddingFromUrl(long knowledgeBaseId, [FromQuery] string url)
        {
            var embeddingTaskId = Guid.NewGuid().ToString("N");
            _knowledgeBaseService.ImportKnowledgeFromUrl(embeddingTaskId, knowledgeBaseId, url);

            return ApiResult.Success(new ImportingTaskResult()
            {
                KnowledgeBaseId = knowledgeBaseId.ToString(),
                ImportingTaskId = embeddingTaskId
            });
        }

        [HttpGet("{knowledgeBaseId}/chunks")]
        public async Task<JsonResult> GetKnowledgeBaseChunks(long knowledgeBaseId)
        {
            var details = await _knowledgeBaseService.GetKnowledgeBaseChunks(knowledgeBaseId);
            return new JsonResult(details);
        }

        [HttpGet("{knowledgeBaseId}/chunks/{fileName}")]
        public async Task<JsonResult> GetKnowledgeBaseChunksWithFileName(long knowledgeBaseId, string fileName = null)
        {
            var details = await _knowledgeBaseService.GetKnowledgeBaseChunks(knowledgeBaseId, fileName);
            return new JsonResult(details);
        }

        [HttpDelete("{knowledgeBaseId}/chunks")]
        public async Task DeleteKnowledgeBaseChunks(long knowledgeBaseId)
        {
            await _knowledgeBaseService.DeleteKnowledgeBaseChunksById(knowledgeBaseId);
        }

        [HttpDelete("{knowledgeBaseId}/chunks/{fileName}")]
        public async Task DeleteKnowledgeBaseChunks(long knowledgeBaseId, string fileName)
        {
            await _knowledgeBaseService.DeleteKnowledgeBaseChunksByFileName(knowledgeBaseId, fileName);
        }

        [HttpGet("list")]
        public async Task<JsonResult> GetKnowledgeBaseDropdownList()
        {
            var list = await _knowledgeBaseService.GetKnowledgeBaseDropdownList();
            return ApiResult.Success(list);
        }

        public override async Task<JsonResult> Create(KnowledgeBase entity)
        {
            var instance = await _knowledgeBaseService.CreateKnowledgeBase(entity);
            return ApiResult.Success(instance);
        }
    }
}
