using PostgreSQL.Embedding.Common.Models.File;

namespace PostgreSQL.Embedding.Services
{
    public interface IFileStorageService
    {
        Task<PutFileStorageResult> PutFileAsync(string bucketName, IFormFile file);
        Task<PutFileStorageResult> PutFileAsync(string bucketName, string fileName);
        Task<GetFileStorageResult> GetFileAsync(string bucketName, string fileId);
        Task GetFileAsync(string bucketName, string fileId, string fileName);
        Task DeleteFileAsync(string bucketName, string fileId);
    }
}
