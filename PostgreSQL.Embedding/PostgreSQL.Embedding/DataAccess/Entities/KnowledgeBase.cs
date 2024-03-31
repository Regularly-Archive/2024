using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("llm_knowledgebase")]
    public class KnowledgeBase : BaseEntity
    {
        [SugarColumn(ColumnName = "avatar")]
        public string Avatar { get; set; }

        [SugarColumn(ColumnName = "intro")]
        public string Intro { get; set; }

        [SugarColumn(ColumnName = "embeddiing_model")]
        public string EmbeddingModel { get; set; }

        [SugarColumn(ColumnName = "service_provider")]
        public int ServiceProvider { get; set; }

        [SugarColumn(ColumnName = "max_tokens_per_paragraph")]
        public int? MaxTokensPerParagraph { get; set; }

        [SugarColumn(ColumnName = "max_tokens_per_line")]
        public int? MaxTokensPerLine { get; set; }

        [SugarColumn(ColumnName = "overlapping_tokens")]
        public int? OverlappingTokens { get; set; }
    }
}
