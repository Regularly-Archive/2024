using PostgreSQL.Embedding.DataAccess.Entities;
using SqlSugar;

namespace PostgreSQL.Embedding.Common.Dtos
{
    public class KnowledgeBaseEditDto:BaseEntity
    {
        public string Avatar { get; set; }
        public string Intro { get; set; }
        public int? MaxTokensPerParagraph { get; set; }
        public int? MaxTokensPerLine { get; set; }
        public int? OverlappingTokens { get; set; }
    }
}
