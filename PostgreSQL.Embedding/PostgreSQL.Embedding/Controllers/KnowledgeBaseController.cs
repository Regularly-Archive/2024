using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.Services;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class KnowledgeBaseController : CrudBaseController<KnowledgeBase>
    {
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IKnowledgeBaseService _knowledgeBaseService;
        private readonly IFullTextSearchService _fullTextSearchService;
        private readonly IFileStorageService _fileStorageService;
        public KnowledgeBaseController(
            IWebHostEnvironment hostingEnvironment,
            IKnowledgeBaseService knowledgeBaseService,
            IFullTextSearchService fullTextSearchService,
            IFileStorageService fileStorageService,
            CrudBaseService<KnowledgeBase> crudBaseService) : base(crudBaseService)
        {
            _webHostEnvironment = hostingEnvironment;
            _knowledgeBaseService = knowledgeBaseService;
            _fullTextSearchService = fullTextSearchService;
            _fileStorageService = fileStorageService;
        }

        [HttpGet("{knowledgeBaseId}/search")]
        public async Task<JsonResult> KnowledgeSearch(long knowledgeBaseId, [FromQuery] string question, [FromQuery] double? minRelevance, [FromQuery] int? limit)
        {
            // Todo: 这里需要重构
            var knowledgeBase = await _crudBaseService.GetById(knowledgeBaseId);
            if (knowledgeBase.RetrievalType == (int)RetrievalType.Vectors)
            {
                var searchResults = await _knowledgeBaseService.SearchAsync(knowledgeBaseId, question);
                return ApiResult.Success(searchResults);
            }
            else
            {
                var searchResults = await _fullTextSearchService.SearchAsync(knowledgeBaseId, question);
                return ApiResult.Success(searchResults);
            }
        }

        [HttpGet("{knowledgeBaseId}/ask")]
        public async Task<JsonResult> KnowledgeBaseAsk(long knowledgeBaseId, [FromQuery] string question, [FromQuery] double minRelevance)
        {
            // Todo: 这里需要重构
            var knowledgeBase = await _crudBaseService.GetById(knowledgeBaseId);
            if (knowledgeBase.RetrievalType == (int)RetrievalType.Vectors)
            {
                var askResults = await _knowledgeBaseService.AskAsync(knowledgeBaseId, question, minRelevance);
                return ApiResult.Success(askResults);
            }
            else
            {
                var searchResults = await _fullTextSearchService.AskAsync(knowledgeBaseId, question);
                return ApiResult.Success(searchResults);
            }
        }

        [HttpPost("{knowledgeBaseId}/embedding/files")]
        public async Task<JsonResult> CreateEmbeddingFromFile(long knowledgeBaseId, List<IFormFile> files)
        {
            var uploadedFiles = new List<string>();
            var embeddingTaskId = Guid.NewGuid().ToString("N");

            if (files == null || !files.Any()) return ApiResult.Failure();

            foreach (var file in files)
            {
                if (file.Length <= 0) continue;
                var result = await _fileStorageService.PutFileAsync(embeddingTaskId, file);
                uploadedFiles.Add(result.FileId);
            }

            if (uploadedFiles.Any()) await _knowledgeBaseService.ImportKnowledgeFromFiles(embeddingTaskId, knowledgeBaseId, uploadedFiles);

            return ApiResult.Success(new ImportingTaskResult()
            {
                KnowledgeBaseId = knowledgeBaseId.ToString(),
                ImportingTaskId = embeddingTaskId
            });
        }

        [HttpPost("{knowledgeBaseId}/embedding/url")]
        public JsonResult CreateEmbeddingFromUrl(long knowledgeBaseId, [FromBody] UrlEmbeddingPayload payload)
        {
            var embeddingTaskId = Guid.NewGuid().ToString("N");
            _knowledgeBaseService.ImportKnowledgeFromUrl(embeddingTaskId, knowledgeBaseId, payload.Url);

            return ApiResult.Success(new ImportingTaskResult()
            {
                KnowledgeBaseId = knowledgeBaseId.ToString(),
                ImportingTaskId = embeddingTaskId
            });
        }

        [HttpPost("{knowledgeBaseId}/embedding/text")]
        public async Task<JsonResult> CreateEmbeddingFromText(long knowledgeBaseId, [FromBody] TextEmbeddingPayload payload)
        {
            var embeddingTaskId = Guid.NewGuid().ToString("N");
            await _knowledgeBaseService.ImportKnowledgeFromText(embeddingTaskId, knowledgeBaseId, payload.Text);

            return ApiResult.Success(new ImportingTaskResult()
            {
                KnowledgeBaseId = knowledgeBaseId.ToString(),
                ImportingTaskId = embeddingTaskId
            });
        }

        [HttpGet("{knowledgeBaseId}/chunks")]
        public async Task<JsonResult> GetKnowledgeBaseChunks(long knowledgeBaseId, [FromQuery] int pageIndex, [FromQuery] int pageSize)
        {
            var details = await _knowledgeBaseService.GetKnowledgeBaseChunks(knowledgeBaseId, null, pageIndex, pageSize);
            return new JsonResult(details);
        }

        [HttpGet("{knowledgeBaseId}/chunks/{fileName}")]
        public async Task<JsonResult> GetKnowledgeBaseChunksWithFileName(long knowledgeBaseId, string fileName, [FromQuery] int pageIndex, [FromQuery] int pageSize)
        {
            var details = await _knowledgeBaseService.GetKnowledgeBaseChunks(knowledgeBaseId, fileName, pageIndex, pageSize);
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

        public override async Task<JsonResult> Create(KnowledgeBase entity)
        {
            var instance = await _knowledgeBaseService.CreateKnowledgeBase(entity);
            return ApiResult.Success(instance);
        }

        public override async Task<JsonResult> Delete(string ids)
        {
            // 删除知识库
            await _crudBaseService.Delete(ids);

            // 删除知识库文档
            var tasks = ids.Split(',').Select(x => _knowledgeBaseService.DeleteKnowledgeBaseChunksById(long.Parse(x)));
            await Task.WhenAll(tasks);

            return ApiResult.Success(new { }, "操作成功");
        }
    }
}
