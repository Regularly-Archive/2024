using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("llm_app")]
    public class LlmApp : BaseEntity
    {
        [SugarColumn(ColumnName = "name")]
        public string Name { get; set; }

        [SugarColumn(ColumnName = "avatar")]
        public string Avatar { get; set; }

        [SugarColumn(ColumnName = "intro")]
        public string Intro { get; set; }

        [SugarColumn(ColumnName = "app_type")]
        public int AppType { get; set; }

        [SugarColumn(ColumnName = "prompt")]
        public string Prompt { get; set; }

        [SugarColumn(ColumnName = "text_model")]
        public string TextModel { get; set; }

        [SugarColumn(ColumnName = "service_provider")]
        public int ServiceProvider { get; set; }

        [SugarColumn(ColumnName = "temperature")]
        public decimal Temperature { get; set; }

        [SugarColumn(IsIgnore = true)]
        public List<long> KnowledgeBaseIds { get; set; }
    }
}
