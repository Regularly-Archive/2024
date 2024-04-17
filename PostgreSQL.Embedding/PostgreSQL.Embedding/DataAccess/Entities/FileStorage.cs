using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("file_storage")]
    public class FileStorage : BaseEntity
    {
        [SugarColumn(ColumnName = "file_id")]
        public string FileId { get; set; }

        [SugarColumn(ColumnName = "file_path", IsNullable = true)]
        public string FilePath { get; set; }

        [SugarColumn(ColumnName = "file_name")]
        public string FileName { get; set; }
    }
}
