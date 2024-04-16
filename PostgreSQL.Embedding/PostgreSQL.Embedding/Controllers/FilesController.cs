using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.Services;
using Microsoft.AspNetCore.Authorization;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/files")]
    [ApiController]
    [AllowAnonymous]
    public class FilesController : ControllerBase
    {
        private readonly IFileStorageService _fileStorageService;
        public FilesController(IFileStorageService fileStorageService)
        {
            _fileStorageService = fileStorageService;
        }

        [HttpPost("{bucketName}/upload")]
        public async Task<JsonResult> Upload(IFormFile file, string bucketName)
        {
            var result = await _fileStorageService.PutFileAsync(bucketName, file);
            return ApiResult.Success(new
            {
                Src = $"/files/{bucketName}/{result.FileId}",
                FileName = result.FileName
            });
        }

        [HttpGet("{bucketName}/{fileId}")]
        public async Task<IActionResult> Download(string bucketName, string fileId)
        {
            var fileStorage = await _fileStorageService.GetFileAsync(bucketName, fileId);
            return File(fileStorage.Content, fileStorage.ContentType);
        }

        [HttpDelete("{bucketName}/{fileId}")]
        public async Task<JsonResult> Delete(string bucketName, string fileId)
        {
            await _fileStorageService.DeleteFileAsync(bucketName, fileId);
            return ApiResult.Success<object>(null);
        }
    }
}
