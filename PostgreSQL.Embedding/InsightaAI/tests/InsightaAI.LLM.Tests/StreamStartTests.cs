using System.Net;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Anthropic;
using InsightaAI.LLM.Gemini;
using InsightaAI.LLM.Models;
using InsightaAI.LLM.OpenAI;

namespace InsightaAI.LLM.Tests;

public sealed class StreamStartTests
{
    [Theory]
    [InlineData("anthropic")]
    [InlineData("openai-response")]
    [InlineData("openai")]
    [InlineData("gemini")]
    public async Task Client_Should_Emit_One_Start_Per_Request(string provider)
    {
        var payload = provider switch
        {
            "anthropic" => "event: message_start\ndata: {\"message\":{\"model\":\"test-model\"}}\n\nevent: content_block_delta\ndata: {\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"hello\"}}\n\n",
            "openai-response" => "event: response.created\ndata: {\"response\":{\"model\":\"test-model\"}}\n\nevent: response.output_text.delta\ndata: {\"output_index\":0,\"delta\":\"hello\"}\n\n",
            "openai" => "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\n",
            _ => "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"hello\"}]}}]}\n\n"
        };
        using var http = new HttpClient(new SseHandler(payload + "data: [DONE]\n\n"));
        var factory = new LlmClientFactory(http)
            .RegisterAdapter(new AnthropicAdapter())
            .RegisterAdapter(new OpenAIResponseAdapter())
            .RegisterAdapter(new OpenAIAdapter())
            .RegisterAdapter(new GeminiAdapter());
        using var client = factory.Create(provider, new ProviderConfig { ApiKey = "test-key" });

        // Reusing a client for another round must still produce its own start event.
        for (var round = 0; round < 2; round++)
        {
            using var stream = client.Streaming(new LlmRequest
            {
                Model = "test-model", Messages = [Message.FromUser("hello")]
            });
            var events = new List<StreamEvent>();
            await foreach (var item in stream)
                events.Add(item);

            var started = Assert.Single(events.OfType<StreamStartEvent>());
            Assert.Same(started, events[0]);
            Assert.Equal("test-model", started.Model);
            Assert.Equal(provider, started.Provider);
            Assert.Equal("hello", Assert.Single(events.OfType<TextDeltaEvent>()).Delta);
            Assert.IsType<DoneEvent>(events[^1]);
            Assert.Equal("test-model", (await stream.GetResponseAsync()).Model);
        }
    }

    private sealed class SseHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "text/event-stream")
            });
    }
}
