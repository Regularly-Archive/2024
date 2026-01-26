using CSnakes.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Utils;
using System.Linq.Expressions;

namespace PostgreSQL.Embedding.Llm.Services.Rerank;

public class BgeRerankService : BaseRerankService, IRerankService
{
    // BAAI/bge-reranker-v2-m3
    private readonly string _modelName = "BAAI/bge-reranker-v2-m3";
    private IBGEReranker _bgeReranker;
    public BgeRerankService(IServiceProvider serviceProvider, IOptions<PythonConfig> options)
        : base(serviceProvider, options)
    {

    }

    protected override void InitModel(IPythonEnvironment environment)
    {
        _logger.LogInformation($"The BGEReranker with model '{_modelName}' is initializing...");

        var envfile = Path.Combine(CSnakeExtensions.HomePath, ".env");
        if (File.Exists(envfile)) File.Delete(envfile);
        File.WriteAllText(envfile, $"RERANKER_MODEL_NAME={_modelName}");

        _bgeReranker = environment.BGEReranker();

        _logger.LogInformation($"The BGEReranker with model '{_modelName}' has been initialized.");
    }

    public override IEnumerable<RerankResult<T>> Sort<T>(string question, List<T> documents, Expression<Func<T, string>> keyExps)
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
