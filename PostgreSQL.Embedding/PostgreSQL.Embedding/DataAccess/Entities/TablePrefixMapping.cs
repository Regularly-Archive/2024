using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("sk_table_prefix_mapping")]
    public class TablePrefixMapping : BaseEntity
    {
        [SugarColumn(ColumnName = "full_name")]
        public string FullName {  get; set; }

        [SugarColumn(ColumnName = "short_name")]
        public string ShortName { get; set; }
    }
}
