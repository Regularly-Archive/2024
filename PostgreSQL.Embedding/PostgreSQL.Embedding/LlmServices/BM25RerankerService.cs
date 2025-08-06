using CSnakes.Runtime;
using Microsoft.Extensions.Options;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Linq.Expressions;

namespace PostgreSQL.Embedding.LlmServices
{
    public class BM25RerankerService : BaseRerankService, IRerankService
    {
        private IBM25Reranker _bm25Reranker;

        public BM25RerankerService(IServiceProvider serviceProvider, IOptions<PythonConfig> options)
            : base(serviceProvider, options)
        {

        }

        protected override void InitModel(IPythonEnvironment environment)
        {
            _logger.LogInformation($"The BM25Reranker is initializing...");

            _bm25Reranker = environment.BM25Reranker();

            _logger.LogInformation($"The BM25Reranker has been initialized.");
        }

        public override IEnumerable<RerankResult<T>> Sort<T>(string question, List<T> documents, Expression<Func<T, string>> keyExps)
        {
            var keyFunc = keyExps.Compile();
            var keyedDocuments = documents.Select(x => keyFunc(x)).ToList();
            var scores = _bm25Reranker.ComputeScores(question, keyedDocuments);

            for (var i = 0; i < documents.Count; i++)
            {
                yield return new RerankResult<T>() { Score = scores[i], Document = documents[i] };
            }
        }
    }
}
