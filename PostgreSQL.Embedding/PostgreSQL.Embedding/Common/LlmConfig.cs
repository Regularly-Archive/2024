namespace PostgreSQL.Embedding.Common
{
    public class LlmConfig
    {
        public LlmServiceProvider Provider { get; set; }
        public string ChatEndpoint {  get; set; }
        public string EmbeddingEndpoint { get; set; }
    }
}
