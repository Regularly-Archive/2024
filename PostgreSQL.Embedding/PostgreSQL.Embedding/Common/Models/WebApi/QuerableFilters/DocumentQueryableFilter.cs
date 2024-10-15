using PostgreSQL.Embedding.DataAccess.Entities;
using SqlSugar;

namespace PostgreSQL.Embedding.Common.Models.WebApi.QuerableFilters
{
    public class DocumentQueryableFilter : IQueryableFilter<DocumentImportRecord>
    {
        public long? KnowledgeBaseId { get; set; }
        public string FileName {  get; set; }
        public int? QueueStatus { get; set; }

        public ISugarQueryable<DocumentImportRecord> Apply(ISugarQueryable<DocumentImportRecord> queryable)
        {
            if (!string.IsNullOrEmpty(FileName))
                queryable = queryable.Where(r => r.FileName.Contains(FileName));

            if (KnowledgeBaseId.HasValue)
                queryable = queryable.Where(r => r.KnowledgeBaseId == KnowledgeBaseId.Value);

            if (QueueStatus.HasValue) 
                queryable = queryable.Where(r => r.QueueStatus == QueueStatus.Value);

            return queryable;
        }
    }
}
