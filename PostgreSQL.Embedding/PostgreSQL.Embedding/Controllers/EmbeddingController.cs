using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using static Microsoft.KernelMemory.DocumentUploadRequest;
using PostgreSQL.Embedding.Services.Training;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/embedding")]
    [ApiController]
    public class EmbeddingController : ControllerBase
    {
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IEmbeddingService _embeddingService;
        public EmbeddingController(IWebHostEnvironment hostingEnvironment, IEmbeddingService embeddingService)
        {
            _webHostEnvironment = hostingEnvironment;
            _embeddingService = embeddingService;
        }

        [HttpPost("files")]
        public async Task CreateEmbeddingFromFile(List<IFormFile> files)
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

            foreach (var uploadedFile in uploadedFiles)
            {
                _embeddingService.AddFileEmbeddingAsync(uploadedFile);
            }
        }

        [HttpGet("text")]
        public async Task CreateEmbeddingFromText([FromQuery] string text)
        {
            await _embeddingService.AddTextEmbeddingAsync(text);
        }

       [HttpGet("url")]
        public async Task CreateEmbedingFromUrl([FromQuery] string url)
        {
            await _embeddingService.AddWebPageEmbeddingAsync(url);
        }

        [HttpGet("search")]
        public async Task SimilaritySearch([FromQuery] string query)
        {
            await _embeddingService.SearchAsync(query, topK: 3);
        }
    }
}
