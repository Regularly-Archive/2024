using Microsoft.KernelMemory;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess.Entities;
using System.Text;

namespace PostgreSQL.Embedding.Handlers
{
    public class TextImportingTaskHandler : BaseImportingTaskHandler, IImportingTaskHandler
    {
        public TextImportingTaskHandler(IServiceProvider serviceProvider)
            : base(serviceProvider) { }

        public override Task<Document> BuildDocument(DocumentImportRecord record)
        {
            var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(record.Content));
            var document = new Document()
                .AddStream(null, memoryStream)
                .AddTag(KernelMemoryTags.TaskId, record.TaskId)
                .AddTag(KernelMemoryTags.FileName, record.FileName)
                .AddTag(KernelMemoryTags.KnowledgeBaseId, record.KnowledgeBaseId.ToString());

            return Task.FromResult(document);
        }

        public override bool IsMatch(DocumentImportRecord record)
        {
            return record.DocumentType == (int)DocumentType.Text;
        }
    }
}
