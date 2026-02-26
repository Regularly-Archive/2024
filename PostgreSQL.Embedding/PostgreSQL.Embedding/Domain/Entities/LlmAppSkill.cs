using SqlSugar;

namespace PostgreSQL.Embedding.Domain.Entities
{
    [SugarTable("llm_app_skills")]
    public class LlmAppSkill : BaseEntity
    {
        [SugarColumn(ColumnName = "app_id", IsNullable = false)]
        public long AppId { get; set; }

        [SugarColumn(ColumnName = "skill_name", IsNullable = false, Length = 200)]
        public string SkillName { get; set; }

        [SugarColumn(ColumnName = "skill_intro", IsNullable = true, Length = 1000)]
        public string? SkillIntro { get; set; }

        [SugarColumn(ColumnName = "storage_path", IsNullable = false, Length = 500)]
        public string StoragePath { get; set; }
    }
}
