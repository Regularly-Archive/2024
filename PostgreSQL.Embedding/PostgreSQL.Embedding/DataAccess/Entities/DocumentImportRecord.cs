using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("document_import_record")]
    public class DocumentImportRecord : BaseEntity
    {
        [SugarColumn(ColumnName = "task_id")]
        public string TaskId { get; set; }

        [SugarColumn(ColumnName = "file_name")]
        public string FileName { get; set; }

        [SugarColumn(ColumnName = "knowledge_base_id")]
        public long KnowledgeBaseId { get; set; }

        [SugarColumn(ColumnName = "queue_status")]
        public int QueueStatus { get; set; }
    }
}
