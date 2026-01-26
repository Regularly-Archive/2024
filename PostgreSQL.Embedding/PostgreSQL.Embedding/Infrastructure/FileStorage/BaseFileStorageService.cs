using Microsoft.AspNetCore.StaticFiles;

namespace PostgreSQL.Embedding.Infrastructure.FileStorage
{
    public class BaseFileStorageService
    {
        public string GetContentType(string fileName)
        {
            var contentType = string.Empty;
            new FileExtensionContentTypeProvider().TryGetContentType(fileName, out contentType);
            return contentType ?? "application/octet-stream";
        }
    }
}
