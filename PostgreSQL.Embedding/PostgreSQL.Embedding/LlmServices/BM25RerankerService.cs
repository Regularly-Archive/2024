using CSnakes.Runtime;
using Microsoft.Extensions.Options;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Linq.Expressions;

namespace PostgreSQL.Embedding.LlmServices
{
    public class BM25RerankerService : IRerankService
    {
        private readonly string _homePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Scripts");

        private readonly string _venvPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Scripts", ".venv");

        private IBM25Reranker _bm25Reranker;

        private readonly ILogger<BM25RerankerService> _logger;
        public BM25RerankerService(IOptions<PythonConfig> options, ILogger<BM25RerankerService> logger)
        {
            _logger = logger;

            var environment = InitPython(options.Value);
            InitModel(environment);
        }

        private IPythonEnvironment InitPython(PythonConfig config)
        {
            _logger.LogInformation($"Python Runtime is initializing: {config.PythonExecute}, Version={config.PythonVersion}...");

            var services = new ServiceCollection().AddLogging();
            services
                .WithPython()
                .WithHome(_homePath)
                .WithVirtualEnvironment(_venvPath)
                .FromFolder(config.PythonExecute, config.PythonVersion)
                .WithPipInstaller();

            var serviceProvider = services.BuildServiceProvider();
            var environment = serviceProvider.GetRequiredService<IPythonEnvironment>();

            _logger.LogInformation($"Python Runtime has been initialized.");

            return environment;
        }

        private void InitModel(IPythonEnvironment environment)
        {
            _logger.LogInformation($"The BM25Reranker is initializing...");

            _bm25Reranker = environment.BM25Reranker();

            _logger.LogInformation($"The BM25Reranker has been initialized.");
        }

        public IEnumerable<RerankResult<T>> Sort<T>(string question, List<T> documents, Expression<Func<T, string>> keyExps)
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
