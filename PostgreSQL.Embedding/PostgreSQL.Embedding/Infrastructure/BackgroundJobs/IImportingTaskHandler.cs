using PostgreSQL.Embedding.Domain.Entities;

namespace PostgreSQL.Embedding.Handlers
{
    public interface IImportingTaskHandler
    {
        Task Handle(DocumentImportRecord record, KnowledgeBase knowledgeBase);
        bool IsMatch(DocumentImportRecord record);
    }
}
