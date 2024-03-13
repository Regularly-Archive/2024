using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class KnowledgeBaseController : ControllerBase
    {
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IKnowledgeBaseService _knowledgeBaseService;
        public KnowledgeBaseController(IWebHostEnvironment hostingEnvironment, IKnowledgeBaseService knowledgeBaseService)
        {
            _webHostEnvironment = hostingEnvironment;
            _knowledgeBaseService = knowledgeBaseService;
        }

        [HttpPost]
        public JsonResult CreateKnowledgeBase(KnowledgeBase knowledgeBase)
        {
            var kb = _knowledgeBaseService.CreateKnowledgeBase(knowledgeBase);
            return new JsonResult(kb);
        }

        [HttpPut]
        public async Task<IActionResult> UpdateKnowledgeBase(KnowledgeBase knowledgeBase)
        {
            await _knowledgeBaseService.UpdateKnowledgeBase(knowledgeBase);
            return Ok();
        }

        [HttpPost("{knowledgeBaseId}/search")]
        public async Task<JsonResult> KnowledgeSearch(long knowledgeBaseId, [FromQuery] string question, [FromQuery] double minRelevance, [FromQuery] int limit)
        {
            var searchResults = await _knowledgeBaseService.SearchAsync(knowledgeBaseId, question, minRelevance, limit);
            return new JsonResult(searchResults);
        }

        [HttpPost("{knowledgeBaseId}/ask")]
        public async Task<JsonResult> KnowledgeBaseAsk(long knowledgeBaseId, [FromQuery] string question, [FromQuery] double minRelevance)
        {
            var searchResults = await _knowledgeBaseService.AskAsync(knowledgeBaseId, question, minRelevance);
            return new JsonResult(searchResults);
        }

        [HttpPost("{knowledgeBaseId}/embedding/files")]
        public JsonResult CreateEmbeddingFromFile(long knowledgeBaseId, List<IFormFile> files)
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
                _knowledgeBaseService.ImportKnowledgeFromFiles(embeddingTaskId, knowledgeBaseId, uploadedFiles);
            }

            return new JsonResult(new ImportingTaskResult()
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

            return new JsonResult(new ImportingTaskResult()
            {
                KnowledgeBaseId = knowledgeBaseId.ToString(),
                ImportingTaskId = embeddingTaskId
            });
        }
    }
}
