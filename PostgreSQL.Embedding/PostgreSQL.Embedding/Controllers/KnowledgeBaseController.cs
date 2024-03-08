using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

        [HttpPost("{knowledgeBaseId}/embedding/files")]
        public async Task CreateEmbeddingFromFile(long knowledgeBaseId, List<IFormFile> files)
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

            
        }

        [HttpPost("{knowledgeBaseId}/embedding/url")]
        public async Task CreateEmbeddingFromUrl(long knowledgeBaseId, [FromQuery] string url)
        {
            var embeddingTaskId = Guid.NewGuid().ToString("N");
            _knowledgeBaseService.ImportKnowledgeFromUrl(embeddingTaskId, knowledgeBaseId, url);
        }
    }
}
