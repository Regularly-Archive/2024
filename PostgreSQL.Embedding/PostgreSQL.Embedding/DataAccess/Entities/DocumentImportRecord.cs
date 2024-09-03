using PostgreSQL.Embedding.Common.Models.Notification;
using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("document_import_records")]
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

        [SugarColumn(ColumnName = "process_start_time", IsNullable = true)]
        public DateTime? ProcessStartTime { get; set; }

        [SugarColumn(ColumnName = "process_end_time", IsNullable = true)]
        public DateTime? ProcessEndTime { get; set; }

        [SugarColumn(ColumnName = "process_duration_time", IsNullable = true)]
        public double? ProcessDuartionTime {  get; set; }

        [SugarColumn(IsIgnore = true)]
        public string KnowledgeBaseName { get; set; }

        [SugarColumn(ColumnName = "document_type")]
        public int DocumentType { get; set; }

        [SugarColumn(ColumnName = "content", IsNullable = true, ColumnDataType = "text")]
        public string Content { get; set; }

        public DocumentReadyEvent CreateDocumentReadyEvent()
        {
            var timeSpan = TimeSpan.FromSeconds(this.ProcessDuartionTime.Value);

            var hours = timeSpan.Hours;
            var minutes = timeSpan.Minutes;
            var seconds = timeSpan.Seconds;

            var formattedDuration = $"{hours.ToString("00")}:{minutes.ToString("00")}:{seconds.ToString("00")}";
            return new DocumentReadyEvent() { Content = $"文档 '{FileName}' 解析完成! 耗时 {formattedDuration}" };
        }

        public DocumentParsingStartedEvent CreateDocumentParsingStartedEvent()
        {
            return new DocumentParsingStartedEvent() { Content = $"文档 '{FileName}' 开始解析..." };
        }
    }
}
