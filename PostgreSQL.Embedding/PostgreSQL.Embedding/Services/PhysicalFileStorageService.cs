
using Microsoft.Extensions.FileProviders;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models.File;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using System.Net;

namespace PostgreSQL.Embedding.Services
{
    public class PhysicalFileStorageService : BaseFileStorageService, IFileStorageService
    {
        private readonly string _rootPath;
        private readonly IFileProvider _fileProvider;
        private readonly IRepository<FileStorage> _fileStorageRepository;

        public PhysicalFileStorageService(IWebHostEnvironment webHostEnvironment, IRepository<FileStorage> fileStorageRepository)
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

        public async Task GetFileAsync(string bucketName, string fileId, string fileName)
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

            using var fileStream = new FileStream(fileName, FileMode.Create, FileAccess.Write);
            fileInfo.CreateReadStream().CopyTo(fileStream);
        }

        public async Task<PutFileStorageResult> PutFileAsync(string bucketName, IFormFile file)
        {
            bucketName = WebUtility.UrlDecode(bucketName);
            bucketName = bucketName.Replace('/', Path.DirectorySeparatorChar);

            var fileId = Guid.NewGuid().ToString("N");
            var fileExt = Path.GetExtension(file.FileName);
            var relativePath = Path.Combine($"{fileId}{fileExt}");
            relativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
            await _fileStorageRepository.AddAsync(new FileStorage()
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

        public async Task<PutFileStorageResult> PutFileAsync(string bucketName, string fileName)
        {
            bucketName = WebUtility.UrlDecode(bucketName);
            bucketName = bucketName.Replace('/', Path.DirectorySeparatorChar);

            var fileId = Guid.NewGuid().ToString("N");
            var fileExt = Path.GetExtension(fileName);
            var relativePath = Path.Combine($"{fileId}{fileExt}");
            relativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
            await _fileStorageRepository.AddAsync(new FileStorage()
            {
                FileId = fileId,
                FilePath = relativePath,
                FileName = Path.GetFileName(fileName),
            });

            var fullPath = Path.Combine(_rootPath, bucketName, relativePath);
            var fullPathDir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(fullPathDir)) Directory.CreateDirectory(fullPathDir);

            File.Copy(fileName, fullPath);
            return new PutFileStorageResult()
            {
                FileId = fileId,
                FileName = Path.GetFileName(fileName)
            };
        }
    }
}
