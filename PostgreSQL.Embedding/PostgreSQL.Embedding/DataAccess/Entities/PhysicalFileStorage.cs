using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("physical_file_storage")]
    public class PhysicalFileStorage : BaseEntity
    {
        [SugarColumn(ColumnName = "file_id")]
        public string FileId { get; set; }

        [SugarColumn(ColumnName = "file_path")]
        public string FilePath { get; set; }

        [SugarColumn(ColumnName = "file_name")]
        public string FileName { get; set; }
    }
}
