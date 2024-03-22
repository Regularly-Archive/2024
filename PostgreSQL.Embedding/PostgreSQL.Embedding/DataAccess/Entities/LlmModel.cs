using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("llm_model")]
    public class LlmModel : BaseEntity
    {
        [SugarColumn(ColumnName = "model_name")]
        public string ModelName { get; set; }

        [SugarColumn(ColumnName = "model_type")]
        public int ModelType { get; set; }

        [SugarColumn(ColumnName = "api_key", IsNullable = true)]
        public string ApiKey { get; set; }

        [SugarColumn(ColumnName = "base_url", IsNullable = true)]
        public string BaseUrl { get; set; }

        [SugarColumn(ColumnName = "service_provider", IsNullable = false)]
        public int ServiceProvider { get; set; }

        [SugarColumn(ColumnName = "is_builtin_model", DefaultValue = "FALSE", IsNullable = true)]
        public bool IsBuiltinModel { get; set; }
    }
}
