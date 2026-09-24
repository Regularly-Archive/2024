using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;

namespace InsightaAI.LLM.Extensions;

/// <summary>
/// IServiceCollection extension methods for LlmClientFactory registration.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="LlmClientFactory"/> as a singleton with <see cref="IHttpClientFactory"/> support.
    /// The named <c>LlmClient</c> HTTP client is equipped with the standard resilience handler
    /// (retry with exponential backoff + jitter, honoring <c>Retry-After</c>) tuned for LLM workloads:
    /// per-attempt and total-request timeouts are disabled because LLM completions can legitimately run for minutes.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional callback to register <see cref="IProviderAdapter"/> instances on the factory.</param>
    /// <param name="configureResilience">Optional callback to override the resilience options after LLM defaults are applied (e.g. for tests).</param>
    /// <param name="configureHttpClientBuilder">Optional callback invoked on the named <see cref="IHttpClientBuilder"/> (e.g. to inject a primary handler in tests).</param>
    /// <param name="retryLogger">
    /// Optional logger for retry events. Retries stay invisible to the end user (no terminal output);
    /// hosts that want a file-side audit trail pass their file logger here. Null disables retry logging.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLlmClientFactory(
        this IServiceCollection services,
        Action<LlmClientFactory>? configure = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null,
        Action<IHttpClientBuilder>? configureHttpClientBuilder = null,
        ILogger? retryLogger = null)
    {
        var builder = services.AddHttpClient("LlmClient");
        builder.AddStandardResilienceHandler(options =>
        {
            ApplyLlmResilienceDefaults(options, retryLogger);
            configureResilience?.Invoke(options);
        });
        configureHttpClientBuilder?.Invoke(builder);

        services.TryAddSingleton<LlmClientFactory>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("LlmClient");

            var factory = new LlmClientFactory(httpClient);
            configure?.Invoke(factory);
            return factory;
        });

        return services;
    }

    /// <summary>
    /// Applies LLM-tuned defaults to the standard resilience options.
    /// Retry targets transient transport failures (429/408/5xx gateway, connection errors);
    /// 4xx request errors are the caller's problem and fail fast.
    /// When <paramref name="retryLogger"/> is provided, each retry is logged at Warning level
    /// (attempt number, reason, backoff delay) — a file-side audit trail invisible to the terminal UI.
    /// </summary>
    internal static void ApplyLlmResilienceDefaults(HttpStandardResilienceOptions options, ILogger? retryLogger = null)
    {
        var retry = options.Retry;
        retry.MaxRetryAttempts = 2; // 3 attempts total
        retry.BackoffType = DelayBackoffType.Exponential;
        retry.UseJitter = true;
        retry.Delay = TimeSpan.FromSeconds(1);
        retry.MaxDelay = TimeSpan.FromSeconds(30);
        retry.ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(response => response.StatusCode is
                HttpStatusCode.RequestTimeout or
                HttpStatusCode.TooManyRequests or
                HttpStatusCode.InternalServerError or
                HttpStatusCode.BadGateway or
                HttpStatusCode.ServiceUnavailable or
                HttpStatusCode.GatewayTimeout);

        if (retryLogger is not null)
        {
            retry.OnRetry = args =>
            {
                var reason = args.Outcome.Result is { } response
                    ? $"HTTP {(int)response.StatusCode}"
                    : args.Outcome.Exception?.GetType().Name ?? "unknown";
                retryLogger.LogWarning(
                    "LLM request retry: attempt {AttemptNumber} failed ({Reason}), next attempt in {RetryDelay:ss\\.fff}s",
                    args.AttemptNumber, reason, args.RetryDelay);
                return ValueTask.CompletedTask;
            };
        }

        // Retry-After honored but always capped at MaxDelay: a provider demanding hours of
        // backoff is useless to an interactive CLI. Native ShouldRetryAfterHeader ignores
        // MaxDelay, so implement the capped variant via DelayGenerator instead.
        retry.ShouldRetryAfterHeader = false;
        retry.DelayGenerator = args =>
        {
            var response = args.Outcome.Result;
            var retryAfter = response?.Headers.RetryAfter;
            var delay = retryAfter?.Delta
                ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
            if (delay is not { } wait || wait <= TimeSpan.Zero)
                return ValueTask.FromResult<TimeSpan?>(null); // fall back to exponential backoff
            return ValueTask.FromResult<TimeSpan?>(
                TimeSpan.FromTicks(Math.Min(wait.Ticks, retry.MaxDelay.Value.Ticks)));
        };

        // LLM completions legitimately run for minutes; the defaults (10s attempt, 30s total)
        // would kill healthy streams. The vendored options interlock: Timeout ∈ [10ms, 1 day],
        // SamplingDuration ≤ 1 day and SamplingDuration ≥ 2 × AttemptTimeout — so the maximum
        // consistent set is 12h timeouts with 1 day sampling. Effectively disabled for a CLI.
        options.TotalRequestTimeout.Timeout = TimeSpan.FromHours(12);
        options.AttemptTimeout.Timeout = TimeSpan.FromHours(12);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromDays(1);

        // RateLimiter (concurrency ceiling) and CircuitBreaker keep package defaults;
        // both are effectively inert at CLI-scale concurrency.
    }
}
