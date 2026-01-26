using CSnakes.Runtime;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.Llm.Abstractions;
using System.Linq.Expressions;

namespace PostgreSQL.Embedding.Llm.Services.Rerank;

public class BaseRerankService : IRerankService
{
    protected ILogger<BaseRerankService> _logger;
    public BaseRerankService(IServiceProvider serviceProvider, IOptions<PythonConfig> options)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger<BaseRerankService>();

        var environment = InitPython(serviceProvider, options.Value);
        InitModel(environment);
    }

    private IPythonEnvironment InitPython(IServiceProvider serviceProvider, PythonConfig config)
    {
        _logger.LogInformation($"Python Runtime is initializing: {config.PythonExecute}, Version={config.PythonVersion}...");

        var environment = serviceProvider.GetRequiredService<IPythonEnvironment>();

        _logger.LogInformation($"Python Runtime has been initialized.");

        return environment;
    }

    public IEnumerable<RerankResult<T>> GetTopN<T>(string question, List<T> documents, Expression<Func<T, string>> keySelector, int? topN)
    {
        var sorted = Sort<T>(question, documents, keySelector).OrderByDescending(x => x.Score).ToList();
        return topN.HasValue ? sorted.Take(topN.Value) : sorted;
    }

    public virtual IEnumerable<RerankResult<T>> Sort<T>(string question, List<T> documents, Expression<Func<T, string>> keySelector)
    {
        throw new NotImplementedException();
    }

    protected virtual void InitModel(IPythonEnvironment environment)
    {

    }
}
