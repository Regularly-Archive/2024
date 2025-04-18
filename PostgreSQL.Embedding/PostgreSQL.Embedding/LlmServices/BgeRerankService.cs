using CSnakes.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.Utils;
using System.Linq.Expressions;

namespace PostgreSQL.Embedding.LlmServices
{
    public class BgeRerankService : IRerankService
    {
        // BAAI/bge-reranker-v2-m3
        private readonly string _modelName = "BAAI/bge-reranker-v2-m3";

        private IBGEReranker _bgeReranker;

        private readonly ILogger<BgeRerankService> _logger;
        public BgeRerankService(IServiceProvider serviceProvider, IOptions<PythonConfig> options, ILogger<BgeRerankService> logger)
        {
            _logger = logger;

            var environment = InitPython(serviceProvider, options.Value);
            InitModel(environment, _modelName);
        }

        private IPythonEnvironment InitPython(IServiceProvider serviceProvider, PythonConfig config)
        {
            _logger.LogInformation($"Python Runtime is initializing: {config.PythonExecute}, Version={config.PythonVersion}...");

            var environment = serviceProvider.GetRequiredService<IPythonEnvironment>();

            _logger.LogInformation($"Python Runtime has been initialized.");

            return environment;
        }

        private void InitModel(IPythonEnvironment environment, string modelName)
        {
            _logger.LogInformation($"The BGEReranker with model '{modelName}' is initializing...");

            var envfile = Path.Combine(CSnakeExtensions.HomePath, ".env");
            if (File.Exists(envfile)) File.Delete(envfile);
            File.WriteAllText(envfile, $"RERANKER_MODEL_NAME={modelName}");

            _bgeReranker = environment.BGEReranker();

            _logger.LogInformation($"The BGEReranker with model '{modelName}' has been initialized.");
        }

        public IEnumerable<RerankResult<T>> Sort<T>(string question, List<T> documents, Expression<Func<T, string>> keyExps)
        {
            var keyFunc = keyExps.Compile();
            var keyedDocuments = documents.Select(x => keyFunc(x)).ToList();
            var scores = _bgeReranker.ComputeScores(question, keyedDocuments);

            for (var i = 0; i < documents.Count; i++)
            {
                yield return new RerankResult<T>() { Score = scores[i], Document = documents[i] };
            }
        }
    }
}
