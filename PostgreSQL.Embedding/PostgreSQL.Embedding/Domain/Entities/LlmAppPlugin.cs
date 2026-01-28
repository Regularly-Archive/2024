using SqlSugar;

namespace PostgreSQL.Embedding.Domain.Entities
{
    [SugarTable("llm_app_plugins")]
    public class LlmAppPlugin : BaseEntity
    {
        [SugarColumn(ColumnName = "app_id", IsNullable = false)]
        public long AppId { get; set; }

        [SugarColumn(ColumnName = "plugin_id", IsNullable = false)]
        public long PluginId { get; set; }

        [SugarColumn(ColumnName = "enabled", ColumnDataType = "boolean", DefaultValue = "TRUE")]
        public bool Enabled { get; set; }
    }
}
