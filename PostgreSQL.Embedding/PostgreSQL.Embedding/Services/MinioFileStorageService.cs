
using Minio;
using Minio.DataModel.Tags;
using PostgreSQL.Embedding.Common.Models.File;

namespace PostgreSQL.Embedding.Services
{
    public class MinioFileStorageService : IFileStorageService
    {
        public readonly IMinioClient _minioClient;

        public MinioFileStorageService(IMinioClient minioClient)
        {
            _minioClient = minioClient;
        }

        public Task DeleteFileAsync(string bucketName, string fileId)
        {
            throw new NotImplementedException();
        }

        public async Task<GetFileStorageResult> GetFileAsync(string bucketName, string fileId)
        {
            throw new NotImplementedException();
        }

        public async Task<PutFileStorageResult> PutFileAsync(string bucketName, IFormFile file)
        {
            throw new NotImplementedException();
        }
    }
}
