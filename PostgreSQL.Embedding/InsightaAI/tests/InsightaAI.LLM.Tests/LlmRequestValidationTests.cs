using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using InsightaAI.LLM.OpenAI;

namespace InsightaAI.LLM.Tests;

public class LlmRequestValidationTests
{
    [Fact]
    public async Task Default_Client_Applies_Request_Middleware_In_Order_Before_Adapter()
    {
        var adapter = new RecordingAdapter();
        using var httpClient = new HttpClient(new StaticResponseHandler());
        var factory = new LlmClientFactory(httpClient).RegisterAdapter(adapter);
        using var client = factory.Create("recording", new ProviderConfig { ApiKey = "test" },
        [
            new DelegateMiddleware(request => request with { Temperature = 0.2 }),
            new DelegateMiddleware(request => LlmRequest.WithResolvedReasoning(
                request with { ReasoningPreference = ReasoningPreference.Off },
                ReasoningConfig.Off() with { OffMode = ReasoningOffMode.EffortNone }))
        ]);

        await client.CompleteAsync(new LlmRequest
        {
            Model = "model",
            Messages = [Message.FromUser("test")]
        });

        Assert.NotNull(adapter.LastRequest);
        Assert.Equal(0.2, adapter.LastRequest.Temperature);
        Assert.Null(adapter.LastRequest.ReasoningPreference);
        Assert.Equal(ReasoningOffMode.EffortNone, adapter.LastRequest.Reasoning?.OffMode);
    }

    [Fact]
    public void Adapter_Rejects_Unresolved_NonDefault_Preference()
    {
        var request = new LlmRequest
        {
            Model = "model",
            Messages = [Message.FromUser("test")],
            ReasoningPreference = ReasoningPreference.Deep
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new OpenAIAdapter().CreateRequest(request, new ProviderConfig { ApiKey = "test" }, stream: false));

        Assert.Contains("must be resolved", exception.Message);
    }

    [Theory]
    [InlineData("thinking")]
    [InlineData("reasoning_effort")]
    [InlineData("budget_tokens")]
    [InlineData("enable_thinking")]
    public void Adapter_Rejects_Reasoning_Control_In_Custom_Provider_Options(string key)
    {
        var request = new LlmRequest
        {
            Model = "model",
            Messages = [Message.FromUser("test")],
            ProviderOptions = new ProviderOptions
            {
                Custom = new Dictionary<string, object> { [key] = true }
            }
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new OpenAIAdapter().CreateRequest(request, new ProviderConfig { ApiKey = "test" }, stream: false));

        Assert.Contains("must be configured through model capabilities", exception.Message);
    }

    [Fact]
    public void Resolved_Request_Clears_Product_Preference()
    {
        var request = LlmRequest.WithResolvedReasoning(new LlmRequest
        {
            Model = "model",
            Messages = [Message.FromUser("test")],
            ReasoningPreference = ReasoningPreference.Off
        }, ReasoningConfig.Off() with { OffMode = ReasoningOffMode.EffortNone });

        Assert.Null(request.ReasoningPreference);
        Assert.Equal(ReasoningOffMode.EffortNone, request.Reasoning?.OffMode);
    }

    private sealed class DelegateMiddleware(Func<LlmRequest, LlmRequest> transform) : ILlmRequestMiddleware
    {
        public LlmRequest Invoke(LlmRequest request) => transform(request);
    }

    private sealed class RecordingAdapter : IProviderAdapter
    {
        public LlmRequest? LastRequest { get; private set; }
        public string Name => "recording";
        public bool SupportsReasoning => true;
        public ReasoningMode SupportedReasoningModes => ReasoningMode.All;

        public HttpRequestMessage CreateRequest(LlmRequest request, ProviderConfig config, bool stream)
        {
            LastRequest = request;
            return new HttpRequestMessage(HttpMethod.Post, "https://example.test/chat");
        }

        public StreamEvent? ParseStreamEvent(string eventType, System.Text.Json.JsonElement data) => null;

        public LlmResponse ParseResponse(System.Text.Json.JsonElement response) => new()
        {
            Model = "model",
            Content = [],
            FinishReason = DoneReason.Complete
        };
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
    }
}
