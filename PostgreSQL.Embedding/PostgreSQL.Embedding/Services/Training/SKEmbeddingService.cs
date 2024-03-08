
using Microsoft.KernelMemory;
using PostgreSQL.Embedding.DataAccess;

namespace PostgreSQL.Embedding.Services.Training
{
    public class SKEmbeddingService : IEmbeddingService
    {
        private readonly MemoryServerless _memoryServerless;
        public SKEmbeddingService(MemoryServerless memoryServerless)
        {
            _memoryServerless = memoryServerless;
        }

        public async Task AddTextEmbeddingAsync(string text)
        {
            var result = await _memoryServerless.ImportTextAsync(text);
        }

        public async Task AddFileEmbeddingAsync(string filePath)
        {
            var result = await _memoryServerless.ImportDocumentAsync(filePath);
        }

        public async Task AddWebPageEmbeddingAsync(string url)
        {
            var result = await _memoryServerless.ImportWebPageAsync(url);
        }

        public async Task SearchAsync(string query, int topK = 3)
        {
            var result = await _memoryServerless.AskAsync(query);
        }
    }
}
