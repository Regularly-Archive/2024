using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("llm_knowledgebase")]
    public class KnowledgeBase : BaseEntity
    {
        [SugarColumn(ColumnName = "avatar", IsNullable = true)]
        public string Avatar { get; set; }

        [SugarColumn(ColumnName = "name")]
        public string Name { get; set; }

        [SugarColumn(ColumnName = "intro", IsNullable = true)]
        public string Intro { get; set; }

        [SugarColumn(ColumnName = "embedding_model")]
        public string EmbeddingModel { get; set; }

        [SugarColumn(ColumnName = "max_tokens_per_paragraph")]
        public int? MaxTokensPerParagraph { get; set; }

        [SugarColumn(ColumnName = "max_tokens_per_line")]
        public int? MaxTokensPerLine { get; set; }

        [SugarColumn(ColumnName = "overlapping_tokens")]
        public int? OverlappingTokens { get; set; }

        [SugarColumn(ColumnName = "retrieval_type", DefaultValue = "0")]
        public int? RetrievalType { get; set; }
    }
}
