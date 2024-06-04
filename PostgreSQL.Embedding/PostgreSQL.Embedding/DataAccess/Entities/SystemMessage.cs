using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("sys_message")]
    public class SystemMessage : BaseEntity
    {
        [SugarColumn(ColumnName = "title")]
        public string Title { get; set; }

        [SugarColumn(ColumnName = "content")]
        public string Content { get; set; }

        [SugarColumn(ColumnName = "type")]
        public string Type { get; set; }

        [SugarColumn(ColumnName = "is_read")]
        public bool IsRead {  get; set; }
    }
}
