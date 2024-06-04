using System.Linq.Expressions;

namespace PostgreSQL.Embedding.LlmServices.Abstration
{
    public interface IRerankService
    {
        double Compute(string a, string b);
        IEnumerable<RerankResult<T>> Sort<T>(string question, List<T> documents, Expression<Func<T,string>> keySelector);
    }

    public class RerankResult<TDocument>
    {
        public double Score { get; set; }
        public TDocument Document { get; set; }
    }
}
