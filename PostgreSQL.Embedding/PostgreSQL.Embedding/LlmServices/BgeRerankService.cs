using CSnakes.Runtime;
using Microsoft.Extensions.Options;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Linq.Expressions;

namespace PostgreSQL.Embedding.LlmServices
{
    public class BgeRerankService : IRerankService
    {
        // BAAI/bge-reranker-v2-m3
        private readonly string _modelName = "BAAI/bge-reranker-v2-m3";
        private IReranker _flagReranker;

        private readonly ILogger<BgeRerankService> _logger;
        public BgeRerankService(IOptions<PythonConfig> options, ILogger<BgeRerankService> logger)
        {
            _logger = logger;

            var environment = InitPython(options.Value);
            InitModel(environment, _modelName);
        }

        private IPythonEnvironment InitPython(PythonConfig config)
        {
            var virtualEnvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Python");
            var dependencyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Scripts", "requirements.txt");

            _logger.LogInformation($"Python Runtime is initializing: {config.PythonExecute}...");

            var homePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Scripts");
            var venvPath = Path.Combine(homePath, ".venv");

            var services = new ServiceCollection().AddLogging();
            services
                .WithPython()
                .WithHome(homePath)
                .WithVirtualEnvironment(venvPath)
                .FromFolder(config.PythonExecute, config.PythonVersion)
                .WithPipInstaller();

            var serviceProvider = services.BuildServiceProvider();
            var environment = serviceProvider.GetRequiredService<IPythonEnvironment>();

            _logger.LogInformation($"Python Runtime has been initialized.");

            return environment;
        }

        private void InitModel(IPythonEnvironment environment, string modelName)
        {
            _logger.LogInformation($"The model '{modelName}' is initializing...");

            Environment.SetEnvironmentVariable("RERANKER_MODEL_NAME", modelName);
            _flagReranker = environment.Reranker();

            _logger.LogInformation($"The model '{modelName}' has been initialized.");
        }

        public IEnumerable<RerankResult<T>> Sort<T>(string question, List<T> documents, Expression<Func<T, string>> keyExps)
        {
            var keyFunc = keyExps.Compile();
            var pairs = documents.Select(x => new List<string> { question, keyFunc(x) }).ToList();
            var scores = _flagReranker.ComputeScores(pairs);

            for (var i = 0; i < documents.Count; i++)
            {
                yield return new RerankResult<T>() { Score = scores[i], Document = documents[i] };
            }
        }
    }
}
