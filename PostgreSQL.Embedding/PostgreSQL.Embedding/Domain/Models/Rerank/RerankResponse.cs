namespace PostgreSQL.Embedding.Domain.Models.Rerank
{
    public class RerankResponse
    {
        public string Query { get; set; }
        public List<RerankScorePair> Scores { get; set; }
    }

    public class RerankScorePair
    {
        public string Document {  get; set; }
        public float Score { get; set; }
    }
}
