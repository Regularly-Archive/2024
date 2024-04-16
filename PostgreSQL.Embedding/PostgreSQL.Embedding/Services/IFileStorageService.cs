using PostgreSQL.Embedding.Common.Models.File;

namespace PostgreSQL.Embedding.Services
{
    public interface IFileStorageService
    {
        Task<PutFileStorageResult> PutFileAsync(string bucketName, IFormFile file);
        Task<GetFileStorageResult> GetFileAsync(string bucketName, string fileId);
        Task DeleteFileAsync(string bucketName, string fileId);
    }
}
