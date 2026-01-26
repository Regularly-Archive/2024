namespace PostgreSQL.Embedding.Domain.Models.Rerank;

public class RerankRequest
{
    public string Query { get; set; }
    public List<string> Documents { get; set; }
}

public class RerankTopNRequest : RerankRequest
{
    public int? TopN { get; set; }
}
