using CSnakes.Runtime;
using Microsoft.Extensions.Options;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Utils;
using System.Linq.Expressions;

namespace PostgreSQL.Embedding.Llm.Services.Rerank;

public class FlashRerankService : BaseRerankService, IRerankService
{
    private IFlashReranker _flashReranker;

    public FlashRerankService(IServiceProvider serviceProvider, IOptions<PythonConfig> options)
        : base(serviceProvider, options)
    {

    }

    protected override void InitModel(IPythonEnvironment environment)
    {
        _logger.LogInformation($"The FlashReranker is initializing...");

        var envfile = Path.Combine(CSnakeExtensions.HomePath, ".env");
        if (File.Exists(envfile)) File.Delete(envfile);
        File.WriteAllText(envfile, $"MODEL_CACHE_DIR=C:\\Users\\Administrator\\Downloads");

        _flashReranker = environment.FlashReranker();

        _logger.LogInformation($"The FlashReranker has been initialized.");
    }

    public override IEnumerable<RerankResult<T>> Sort<T>(string question, List<T> documents, Expression<Func<T, string>> keyExps)
    {
        var keyFunc = keyExps.Compile();
        var keyedDocuments = documents.Select(x => keyFunc(x)).ToList();
        var scores = _flashReranker.ComputeScores(question, keyedDocuments);

        for (var i = 0; i < documents.Count; i++)
        {
            yield return new RerankResult<T>() { Score = scores[i], Document = documents[i] };
        }
    }
}
