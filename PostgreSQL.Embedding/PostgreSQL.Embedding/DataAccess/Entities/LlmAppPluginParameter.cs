using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("llm_app_plugin_parameters")]
    public class LlmAppPluginParameter : BaseEntity
    {
        [SugarColumn(ColumnName = "app_id", IsNullable = false)]
        public long AppId { get; set; }

        [SugarColumn(ColumnName = "plugin_id", IsNullable = false)]
        public long PluginId { get; set; }

        [SugarColumn(ColumnName = "parameter_name", IsNullable = false)]
        public string ParameterName { get; set; }

        [SugarColumn(ColumnName = "parameter_value", IsNullable = false)]
        public string ParameterValue { get; set; }
    }
}
