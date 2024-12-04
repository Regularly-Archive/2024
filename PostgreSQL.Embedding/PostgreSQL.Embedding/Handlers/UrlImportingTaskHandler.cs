using Microsoft.KernelMemory;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.Utils;
using SqlSugar;
using System.Text;

namespace PostgreSQL.Embedding.Handlers
{
    public class UrlImportingTaskHandler : BaseImportingTaskHandler, IImportingTaskHandler
    {
        public UrlImportingTaskHandler(IServiceProvider serviceProvider)
            : base(serviceProvider) { }

        public override async Task<Document> BuildDocument(DocumentImportRecord record)
        {
            using var serviceScope = _serviceProvider.CreateScope();
            var serviceProvider = serviceScope.ServiceProvider;
            var repository = serviceProvider.GetRequiredService<IRepository<DocumentImportRecord>>();

            // 延迟解析网页内容
            if (string.IsNullOrEmpty(record.Content))
            {
                var extracted = await WebPageExtractor.ExtractWebPageAsync(record.FileName, null);
                record.Content = JsonConvert.SerializeObject(extracted);
                await repository.UpdateAsync(record);
            } 

            var extractionResult = JsonConvert.DeserializeObject<WebPageExtractionResult>(record.Content);

            var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(extractionResult.Content));
            var document = new Document()
                .AddStream(null, memoryStream)
                .AddTag(KernelMemoryTags.TaskId, record.TaskId)
                .AddTag(KernelMemoryTags.FileName, record.FileName)
                .AddTag(KernelMemoryTags.KnowledgeBaseId, record.KnowledgeBaseId.ToString())
                .AddTag(KernelMemoryTags.Url, extractionResult.Url);

            return document;
        }

        public override bool IsMatch(DocumentImportRecord record)
        {
            return record.DocumentType == (int)DocumentType.Url;
        }
    }
}
