using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("llm_app_knowledges")]
    public class LlmAppKnowledge : BaseEntity
    {
        [SugarColumn(ColumnName ="app_id", IsNullable = false)]
        public long AppId { get; set; }

        [SugarColumn(ColumnName = "knowledge_base_id", IsNullable = false)]
        public long KnowledgeBaseId { get; set; }
    }
}
