using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Extensions;
using InsightaAI.LLM.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;

namespace InsightaAI.LLM.Tests;

/// <summary>
/// Verifies that the named "LlmClient" HTTP client retries transient failures
/// (429 / 5xx gateway / connection errors) via the standard resilience handler,
/// while 4xx request errors fail fast. Retry happens strictly before any stream
/// content is consumed, so streaming consumers never see duplicated events.
/// </summary>
public class LlmResilienceTests
{
    private const string DoneStream = "data: [DONE]\n\n";

    [Fact]
    public async Task Complete_Retries_On_429_Then_Succeeds()
    {
        var (services, handler) = Build([HttpStatusCode.TooManyRequests, HttpStatusCode.OK], okBody: "{}");
        using var _ = services;

        var client = CreateClient(services);
        var response = await client.CompleteAsync(NewRequest());

        Assert.Equal(DoneReason.Complete, response.FinishReason);
        Assert.Equal(2, handler.Attempts);
    }

    [Fact]
    public async Task Complete_Does_Not_Retry_On_401()
    {
        var (services, handler) = Build([HttpStatusCode.Unauthorized]);
        using var _ = services;

        var client = CreateClient(services);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.CompleteAsync(NewRequest()));
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task Complete_Exhausts_Retries_On_Persistent_429()
    {
        var (services, handler) = Build([HttpStatusCode.TooManyRequests]);
        using var _ = services;

        var client = CreateClient(services);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.CompleteAsync(NewRequest()));
        Assert.Equal(3, handler.Attempts); // 1 initial + 2 retries
    }

    [Fact]
    public async Task Stream_Retries_Before_First_Event()
    {
        var (services, handler) = Build(
            [HttpStatusCode.ServiceUnavailable, HttpStatusCode.TooManyRequests, HttpStatusCode.OK]);
        using var _ = services;

        var client = CreateClient(services);
        var events = await CollectAsync(client.Streaming(NewRequest()));

        Assert.Equal(3, handler.Attempts);
        // Exactly one StreamStartEvent proves no attempt re-entered the stream body.
        Assert.Single(events.OfType<StreamStartEvent>());
        var done = Assert.Single(events.OfType<DoneEvent>());
        Assert.Equal(DoneReason.Complete, done.Reason);
    }

    [Fact]
    public async Task Stream_Exhaustion_Emits_Error_Then_Done()
    {
        var (services, handler) = Build([HttpStatusCode.TooManyRequests]);
        using var _ = services;

        var client = CreateClient(services);
        var events = await CollectAsync(client.Streaming(NewRequest()));

        Assert.Equal(3, handler.Attempts);
        Assert.Contains(events, e => e is ErrorEvent);
        var done = Assert.Single(events.OfType<DoneEvent>());
        Assert.Equal(DoneReason.Error, done.Reason);
    }

    [Fact]
    public async Task Retry_After_Header_Is_Capped_At_MaxDelay()
    {
        var (services, handler) = Build(
            [HttpStatusCode.TooManyRequests, HttpStatusCode.OK],
            resilience =>
            {
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(1);
                resilience.Retry.MaxDelay = TimeSpan.FromMilliseconds(50);
                resilience.Retry.UseJitter = false;
            },
            withRetryAfterSeconds: 3600,
            okBody: "{}");
        using var _ = services;

        var client = CreateClient(services);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stopwatch = Stopwatch.StartNew();

        await client.CompleteAsync(NewRequest(), cts.Token);

        stopwatch.Stop();
        Assert.Equal(2, handler.Attempts);
        // Uncapped Retry-After (3600s) would trip the 15s CTS; capped must be fast.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Retry took {stopwatch.Elapsed}; Retry-After was not capped.");
    }

    [Fact]
    public void Llm_Resilience_Defaults_Are_Tuned()
    {
        var options = new HttpStandardResilienceOptions();
        ServiceCollectionExtensions.ApplyLlmResilienceDefaults(options);

        Assert.Equal(2, options.Retry.MaxRetryAttempts); // 3 attempts total
        Assert.Equal(DelayBackoffType.Exponential, options.Retry.BackoffType);
        Assert.True(options.Retry.UseJitter);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Retry.Delay);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Retry.MaxDelay);
        Assert.True(options.Retry.ShouldRetryAfterHeader == false);
        Assert.NotNull(options.Retry.DelayGenerator);

        // LLM completions legitimately run for minutes: timeouts set to the maximum set
        // allowed by option interlocking (sampling ≤ 1 day, sampling ≥ 2 × attempt timeout).
        Assert.Equal(TimeSpan.FromHours(12), options.TotalRequestTimeout.Timeout);
        Assert.Equal(TimeSpan.FromHours(12), options.AttemptTimeout.Timeout);
        Assert.Equal(TimeSpan.FromDays(1), options.CircuitBreaker.SamplingDuration);

        // Transient statuses are retryable; request errors are not.
        Assert.True(IsRetryable(options, HttpStatusCode.TooManyRequests));
        Assert.True(IsRetryable(options, HttpStatusCode.RequestTimeout));
        Assert.True(IsRetryable(options, HttpStatusCode.InternalServerError));
        Assert.True(IsRetryable(options, HttpStatusCode.BadGateway));
        Assert.True(IsRetryable(options, HttpStatusCode.ServiceUnavailable));
        Assert.True(IsRetryable(options, HttpStatusCode.GatewayTimeout));
        Assert.False(IsRetryable(options, HttpStatusCode.BadRequest));
        Assert.False(IsRetryable(options, HttpStatusCode.Unauthorized));
        Assert.False(IsRetryable(options, HttpStatusCode.Forbidden));
        Assert.False(IsRetryable(options, HttpStatusCode.NotFound));
        Assert.False(IsRetryable(options, HttpStatusCode.UnprocessableEntity));
    }

    private static bool IsRetryable(HttpStandardResilienceOptions options, HttpStatusCode status)
    {
        var context = ResilienceContextPool.Shared.Get();
        try
        {
            var outcome = Outcome.FromResult(new HttpResponseMessage(status));
            var args = new RetryPredicateArguments<HttpResponseMessage>(context, outcome, 0);
            return options.Retry.ShouldHandle(args).GetAwaiter().GetResult();
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    private static (ServiceProvider Services, ScriptedHandler Handler) Build(
        HttpStatusCode[] statuses,
        Action<HttpStandardResilienceOptions>? configureResilience = null,
        int? withRetryAfterSeconds = null,
        string okBody = DoneStream)
    {
        var handler = new ScriptedHandler(statuses, withRetryAfterSeconds, okBody);
        var services = new ServiceCollection();
        services.AddLlmClientFactory(
            factory => factory.RegisterAdapter(new EchoAdapter()),
            configureResilience,
            builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));
        return (services.BuildServiceProvider(), handler);
    }

    private static ILlmClient CreateClient(ServiceProvider services) =>
        services.GetRequiredService<LlmClientFactory>()
            .Create("echo", new ProviderConfig { ApiKey = "test" });

    private static LlmRequest NewRequest() => new()
    {
        Model = "model",
        Messages = [Message.FromUser("hi")]
    };

    private static async Task<List<StreamEvent>> CollectAsync(
        IAsyncEnumerable<StreamEvent> events,
        CancellationToken cancellationToken = default)
    {
        var collected = new List<StreamEvent>();
        await foreach (var streamEvent in events.WithCancellation(cancellationToken))
            collected.Add(streamEvent);
        return collected;
    }

    /// <summary>Returns scripted responses in order; the last status repeats when the script runs out.</summary>
    private sealed class ScriptedHandler(
        HttpStatusCode[] statuses,
        int? retryAfterSeconds,
        string okBody) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = statuses[Math.Min(Attempts, statuses.Length - 1)];
            Attempts++;

            var response = new HttpResponseMessage(status);
            response.Content = status == HttpStatusCode.OK
                ? new StringContent(okBody)
                : new StringContent("""{"error":{"code":"1302","message":"rate limited"}}""");
            if (status == HttpStatusCode.TooManyRequests && retryAfterSeconds is { } seconds)
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            return Task.FromResult(response);
        }
    }

    private sealed class EchoAdapter : IProviderAdapter
    {
        public string Name => "echo";
        public bool SupportsReasoning => true;
        public ReasoningMode SupportedReasoningModes => ReasoningMode.All;

        public HttpRequestMessage CreateRequest(LlmRequest request, ProviderConfig config, bool stream) =>
            new(HttpMethod.Post, "https://example.test/v1/chat")
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            };

        public StreamEvent? ParseStreamEvent(string eventType, System.Text.Json.JsonElement data) => null;

        public LlmResponse ParseResponse(System.Text.Json.JsonElement response) => new()
        {
            Model = "model",
            Content = [],
            FinishReason = DoneReason.Complete
        };
    }
}
