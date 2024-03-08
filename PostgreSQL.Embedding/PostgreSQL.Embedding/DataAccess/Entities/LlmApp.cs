namespace PostgreSQL.Embedding.DataAccess.Entities
{
    public class LlmApp : BaseEntity
    {
        public string Avatar { get; set; }
        public string Intro { get; set; }
        public int AppType { get; set; }
        public string Prompt { get; set; }
        public string TextModel { get; set; }
        public int ServiceProvider { get; set; }
        public int Temperature { get; set; }
    }
}
