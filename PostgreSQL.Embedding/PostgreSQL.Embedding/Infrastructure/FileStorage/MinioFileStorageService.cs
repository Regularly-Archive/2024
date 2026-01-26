
using Minio;
using Minio.DataModel.Args;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.File;
using PostgreSQL.Embedding.Infrastructure.DataAccess;

namespace PostgreSQL.Embedding.Infrastructure.FileStorage
{
    public class MinioFileStorageService : BaseFileStorageService, IFileStorageService
    {
        private readonly IMinioClient _minioClient;
        private readonly IRepository<Domain.Entities.FileStorage> _fileStorageRepository;

        public MinioFileStorageService(IMinioClient minioClient, IRepository<Domain.Entities.FileStorage> fileStorageRepository)
        {
            _minioClient = minioClient;
            _fileStorageRepository = fileStorageRepository;
        }

        public async Task DeleteFileAsync(string bucketName, string fileId)
        {
            var fileStorage = await _fileStorageRepository.FindAsync(x => x.FileId == fileId);
            if (fileStorage == null)
                throw new Exception($"The file '{fileId}' is not exist.");

            await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(bucketName)
                .WithObject(fileStorage.FileName)
            );
            await _fileStorageRepository.DeleteAsync(x => x.FileId == fileId);
        }

        public async Task<GetFileStorageResult> GetFileAsync(string bucketName, string fileId)
        {
            var fileStorage = await _fileStorageRepository.FindAsync(x => x.FileId == fileId);
            if (fileStorage == null)
                throw new Exception($"The file '{fileId}' is not exist.");

            var result = new GetFileStorageResult();
            var response = await _minioClient.GetObjectAsync(new GetObjectArgs()
                .WithBucket(bucketName)
                .WithObject(fileStorage.FileName)
                .WithMatchETag(fileId)
                .WithCallbackStream(stream =>
                {
                    var memoryStream = new MemoryStream();
                    stream.CopyTo(memoryStream);
                    memoryStream.Position = 0;
                    result.Content = memoryStream;
                })
            );

            result.FileName = response.ObjectName;
            result.ContentType = response.ContentType;
            return result;
        }

        public async Task GetFileAsync(string bucketName, string fileId, string fileName)
        {
            var fileStorage = await _fileStorageRepository.FindAsync(x => x.FileId == fileId);
            if (fileStorage == null)
                throw new Exception($"The file '{fileId}' is not exist.");

            var response = await _minioClient.GetObjectAsync(new GetObjectArgs()
                .WithBucket(bucketName)
                .WithObject(fileStorage.FileName)
                .WithMatchETag(fileId)
                .WithFile(fileName)
            );
        }

        public async Task<PutFileStorageResult> PutFileAsync(string bucketName, IFormFile file)
        {
            await EnsureBucketExists(bucketName);

            var result = new PutFileStorageResult();
            var response = await _minioClient.PutObjectAsync(new PutObjectArgs()
                .WithBucket(bucketName)
                .WithObject(file.FileName)
                .WithStreamData(file.OpenReadStream())
                .WithObjectSize(file.Length)
                .WithContentType(GetContentType(file.FileName))
            );

            result.FileName = file.FileName;
            result.FileId = response.Etag.Replace("\"", "");

            await CreateFileStorage(result.FileId, result.FileName);

            return result;
        }

        public async Task<PutFileStorageResult> PutFileAsync(string bucketName, string fileName)
        {
            await EnsureBucketExists(bucketName);

            if (!File.Exists(fileName))
                throw new ArgumentException($"The file '{fileName}' is not exist.");

            var result = new PutFileStorageResult();
            var response = await _minioClient.PutObjectAsync(new PutObjectArgs()
                .WithBucket(bucketName)
                .WithObject(Path.GetFileName(fileName))
                .WithFileName(fileName)
                .WithContentType(GetContentType(fileName))
            );

            result.FileName = Path.GetFileName(fileName);
            result.FileId = response.Etag.Replace("\"", "");

            await CreateFileStorage(result.FileId, result.FileName);

            return result;
        }

        private async Task EnsureBucketExists(string bucketName)
        {
            bool exists = await _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName));
            if (!exists) await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));
        }

        private async Task CreateFileStorage(string fileId, string fileName)
        {
            var exists = await _fileStorageRepository.ExistsAsync(x => x.FileId == fileId);
            if (!exists)
            {
                await _fileStorageRepository.AddAsync(new Domain.Entities.FileStorage()
                {
                    FileId = fileId,
                    FileName = fileName,
                });
            }
        }
    }
}
