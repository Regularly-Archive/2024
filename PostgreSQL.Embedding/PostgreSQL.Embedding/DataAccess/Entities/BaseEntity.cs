using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    public class BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")]
        public virtual long Id { get; set; }
        [SugarColumn(ColumnName = "created_at")]
        public virtual DateTime? CreatedAt { get; set; }
        [SugarColumn(ColumnName = "created_by")]
        public virtual string CreatedBy { get; set; }
        [SugarColumn(ColumnName = "updated_at")]
        public virtual DateTime? UpdatedAt { get; set; }
        [SugarColumn(ColumnName = "updated_by")]
        public virtual string UpdatedBy { get; set; }
    }
}
