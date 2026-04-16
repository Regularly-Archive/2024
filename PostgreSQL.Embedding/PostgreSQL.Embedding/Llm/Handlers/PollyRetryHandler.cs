using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace PostgreSQL.Embedding.Llm.Handlers
{
    public class PollyRetryHandler : DelegatingHandler
    {
        private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;
        private readonly ILogger<PollyRetryHandler> _logger;

        public PollyRetryHandler(HttpClientHandler innerHandler, ILogger<PollyRetryHandler> logger)
            : this(innerHandler, CreateDefaultOptions(logger))
        {
        }

        public PollyRetryHandler(HttpClientHandler innerHandler, RetryStrategyOptions<HttpResponseMessage> options, ILogger<PollyRetryHandler>? logger = null)
        {
            InnerHandler = innerHandler;
            _logger = logger ?? LoggerFactory.Create(b => b.AddConsole()).CreateLogger<PollyRetryHandler>();

            options.ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .Handle<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
                .HandleResult(r => (int)r.StatusCode >= 500);

            options.OnRetry = args =>
            {
                var delay = args.RetryDelay;
                var attempt = args.AttemptNumber;
                var reason = args.Outcome;

                if (reason.Exception != null)
                {
                    _logger.LogWarning("Retry ({Attempt}/{Total}) after {Delay}s due to: {Message}", attempt, options.MaxRetryAttempts, delay.TotalSeconds, reason.Exception.Message);
                }
                else if (reason.Result != null)
                {
                    _logger.LogWarning("Retry ({Attempt}/{Total}) after {Delay}s due to HTTP StatusCode: {StatusCode}", attempt, options.MaxRetryAttempts, delay.TotalSeconds, (int)reason.Result.StatusCode);
                }

                return ValueTask.CompletedTask;
            };

            _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
                .AddRetry(options)
                .Build();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return await _pipeline.ExecuteAsync(
                async ct => await base.SendAsync(request, ct),
                cancellationToken);
        }

        private static RetryStrategyOptions<HttpResponseMessage> CreateDefaultOptions(ILogger<PollyRetryHandler> logger)
        {
            return new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 10,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            };
        }
    }
}