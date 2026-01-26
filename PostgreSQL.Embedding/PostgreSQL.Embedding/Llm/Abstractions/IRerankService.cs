using System.Linq.Expressions;

namespace PostgreSQL.Embedding.Llm.Abstractions
{
    public interface IRerankService
    {
        IEnumerable<RerankResult<T>> Sort<T>(string question, List<T> documents, Expression<Func<T,string>> keySelector);
        IEnumerable<RerankResult<T>> GetTopN<T>(string question, List<T> documents, Expression<Func<T, string>> keySelector, int? topN);
    }

    public class RerankResult<TDocument>
    {
        public double Score { get; set; }
        public TDocument Document { get; set; }
    }
}
