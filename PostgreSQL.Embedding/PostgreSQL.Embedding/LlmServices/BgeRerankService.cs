using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.LlmServices.Abstration;
using Python.Runtime;
using System.Linq.Expressions;

namespace PostgreSQL.Embedding.LlmServices
{
    public class BgeRerankService : IRerankService
    {
        private readonly string _modelName = "AI-ModelScope/bge-reranker-v2-gemma";
        private readonly dynamic _flagReranker;

        private readonly ILogger<BgeRerankService> _logger;
        public BgeRerankService(IOptions<PythonConfig> options, ILogger<BgeRerankService> logger)
        {
            _logger = logger;
            InitPython(options.Value);
            _flagReranker = InitModel(_modelName);
        }

        private void InitPython(PythonConfig config)
        {
            if (config != null && !string.IsNullOrEmpty(config.LibraryPath))
                Runtime.PythonDLL = config.LibraryPath;

            _logger.LogInformation($"Python Runtime is initializing: {config.LibraryPath}...");
            PythonEngine.Initialize();
            PythonEngine.BeginAllowThreads();
            _logger.LogInformation($"Python Runtime has been initialized.");
        }

        private dynamic InitModel(string modelName)
        {
            using (Py.GIL())
            {
                _logger.LogInformation($"The model '{modelName}' is initializing...");

                dynamic modelscope = Py.Import("modelscope");
                dynamic flagEmbedding = Py.Import("FlagEmbedding");

                dynamic rerankModelDir = modelscope.snapshot_download(modelName, revision: "master");
                dynamic flagReranker = flagEmbedding.FlagReranker(rerankModelDir, use_fp16: true);

                _logger.LogInformation($"The model '{modelName}' has been initialized.");

                return flagReranker;
            }
        }

        public double Compute(string a, string b)
        {
            using (Py.GIL())
            {
                var pyList = new PyList();
                pyList.Append(a.ToPython());
                pyList.Append(b.ToPython());

                PyObject result = _flagReranker.compute_score(pyList, normalize: true);
                return result.As<double>();
            }
        }

        public IEnumerable<RerankResult<T>> Sort<T>(string question, List<T> documents, Expression<Func<T, string>> keyExps)
        {
            var keyFunc = keyExps.Compile();
            using (Py.GIL())
            {
                var pyList = new PyList();
                foreach (var document in documents)
                {
                    var pair = new PyList();
                    pair.Append(question.ToPython());
                    pair.Append(keyFunc(document).ToPython());
                    pyList.Append(pair.ToPython());
                }

                PyObject result = _flagReranker.compute_score(pyList, normalize: true);
                var scores = result.As<PyList>();

                for (var i = 0; i < documents.Count; i++)
                {
                    yield return new RerankResult<T>() { Score = scores[i].As<double>(), Document = documents[i] };
                }
            }
        }
    }
}
