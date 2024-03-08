namespace PostgreSQL.Embedding.DataAccess.Entities
{
    public class KnowledgeBase : BaseEntity
    {
        public string Avatar { get; set; }
        public string Intro { get; set; }
        public string EmbeddingModel { get; set; }
        public int ServiceProvider { get; set; }
    }
}
