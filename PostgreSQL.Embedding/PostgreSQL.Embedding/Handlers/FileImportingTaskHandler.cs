
using Microsoft.KernelMemory;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.Handlers
{
    public class FileImportingTaskHandler : BaseImportingTaskHandler, IImportingTaskHandler
    {
        public FileImportingTaskHandler(IServiceProvider serviceProvider)
            : base(serviceProvider) { }

        public override Task<Document> BuildDocument(DocumentImportRecord record)
        {
            var tags = new TagCollection
            {
                { KernelMemoryTags.TaskId, record.TaskId },
                { KernelMemoryTags.FileName, record.FileName },
                { KernelMemoryTags.KnowledgeBaseId, record.KnowledgeBaseId.ToString() },
            };

            var document = new Document(
                tags: tags,
                filePaths: new List<string> { record.Content }
            );

            return Task.FromResult(document);
        }

        public override bool IsMatch(DocumentImportRecord record)
        {
            return record.DocumentType == (int)DocumentType.File;
        }
    }
}
