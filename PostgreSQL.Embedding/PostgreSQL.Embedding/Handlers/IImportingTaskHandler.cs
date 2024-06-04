using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.Handlers
{
    public interface IImportingTaskHandler
    {
        Task Handle(DocumentImportRecord record, KnowledgeBase knowledgeBase);
        bool IsMatch(DocumentImportRecord record);
    }
}
