
using Microsoft.Extensions.FileProviders;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models.File;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using System.Net;
using System.Text.Encodings.Web;
using static Microsoft.KernelMemory.DocumentUploadRequest;

namespace PostgreSQL.Embedding.Services
{
    public class PhysicalFileStorageService : IFileStorageService
    {
        private readonly string _rootPath;
        private readonly IFileProvider _fileProvider;
        private readonly IRepository<PhysicalFileStorage> _fileStorageRepository;

        public PhysicalFileStorageService(IWebHostEnvironment webHostEnvironment, IRepository<PhysicalFileStorage> fileStorageRepository)
        {
            _rootPath = Path.Combine(webHostEnvironment.ContentRootPath, Constants.DefaultUploadFolder);
            _fileProvider = new PhysicalFileProvider(_rootPath);
            _fileStorageRepository = fileStorageRepository;
        }

        public async Task DeleteFileAsync(string bucketName, string fileId)
        {
            var fileStorage = await _fileStorageRepository.SingleOrDefaultAsync(x => x.FileId == fileId);
            if (fileStorage == null)
                throw new Exception($"The file '{fileId}' is not exist.");

            bucketName = WebUtility.UrlDecode(bucketName);
            bucketName = bucketName.Replace('/', Path.DirectorySeparatorChar);

            var relativePath = Path.Combine(bucketName, fileStorage.FilePath);
            var fileInfo = _fileProvider.GetFileInfo(relativePath);
            if (!fileInfo.Exists)
                throw new ArgumentException($"The file '{relativePath}' is not exist.");

            File.Delete(fileInfo.PhysicalPath);
            await _fileStorageRepository.DeleteAsync(x => x.FileId == fileId);
        }

        public async Task<GetFileStorageResult> GetFileAsync(string bucketName, string fileId)
        {
            var fileStorage = await _fileStorageRepository.SingleOrDefaultAsync(x => x.FileId == fileId);
            if (fileStorage == null)
                throw new Exception($"The file '{fileId}' is not exist.");

            bucketName = WebUtility.UrlDecode(bucketName);
            bucketName = bucketName.Replace('/', Path.DirectorySeparatorChar);

            var relativePath = Path.Combine(bucketName, fileStorage.FilePath);
            var fileInfo = _fileProvider.GetFileInfo(relativePath);
            if (!fileInfo.Exists)
                throw new ArgumentException($"The file '{fileStorage.FileName}' is not exist.");

            return new GetFileStorageResult()
            {
                Content = fileInfo.CreateReadStream(),
                ContentType = GetContentType(fileInfo.PhysicalPath),
                FileName = fileStorage.FileName,
            };
        }

        public async Task<PutFileStorageResult> PutFileAsync(string bucketName, IFormFile file)
        {
            bucketName = WebUtility.UrlDecode(bucketName);
            bucketName = bucketName.Replace('/', Path.DirectorySeparatorChar);

            var fileId = Guid.NewGuid().ToString("N");
            var fileExt = Path.GetExtension(file.FileName);
            var relativePath = Path.Combine($"{fileId}{fileExt}");
            relativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
            await _fileStorageRepository.AddAsync(new PhysicalFileStorage()
            {
                FileId = fileId,
                FilePath = relativePath,
                FileName = file.FileName,
            });

            var fullPath = Path.Combine(_rootPath, bucketName, relativePath);
            var fullPathDir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(fullPathDir)) Directory.CreateDirectory(fullPathDir);

            using var fileStream = new FileStream(fullPath, FileMode.Create);
            file.CopyTo(fileStream);

            return new PutFileStorageResult()
            {
                FileId = fileId,
                FileName = file.FileName
            };
        }

        private string GetContentType(string fileName)
        {
            if (fileName.Contains(".jpg"))
            {
                return "image/jpg";
            }
            else if (fileName.Contains(".jpeg"))
            {
                return "image/jpeg";
            }
            else if (fileName.Contains(".png"))
            {
                return "image/png";
            }
            else if (fileName.Contains(".gif"))
            {
                return "image/gif";
            }
            else if (fileName.Contains(".pdf"))
            {
                return "application/pdf";
            }
            else
            {
                return "application/octet-stream";
            }
        }
    }
}
