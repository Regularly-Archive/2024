using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("llm_apps")]
    public class LlmApp : BaseEntity
    {
        [SugarColumn(ColumnName = "name")]
        public string Name { get; set; }

        [SugarColumn(ColumnName = "avatar", IsNullable = true)]
        public string Avatar { get; set; }

        [SugarColumn(ColumnName = "intro", IsNullable = true)]
        public string Intro { get; set; }


        [SugarColumn(ColumnName = "app_type")]
        public int AppType { get; set; }

        [SugarColumn(ColumnName = "prompt", IsNullable = true)]
        public string Prompt { get; set; }

        [SugarColumn(ColumnName = "welcome", IsNullable = true)]
        public string Welcome { get; set; }

        [SugarColumn(ColumnName = "text_model")]
        public string TextModel { get; set; }

        [SugarColumn(ColumnName = "temperature")]
        public decimal Temperature { get; set; }

        [SugarColumn(ColumnName = "enable_rewrite", IsNullable = false, DefaultValue = "FALSE")]
        public bool EnableRewrite { get; set; }

        [SugarColumn(ColumnName = "enable_rerank", IsNullable = false, DefaultValue = "FALSE")]
        public bool EnableRerank { get; set; }

        [SugarColumn(ColumnName = "max_message_rounds", IsNullable = false, DefaultValue = "10")]
        public int MaxMessageRounds { get; set; }

        [SugarColumn(IsIgnore = true)]
        public List<long> KnowledgeBaseIds { get; set; }
    }
}
