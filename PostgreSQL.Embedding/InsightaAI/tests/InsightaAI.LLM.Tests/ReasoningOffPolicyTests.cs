using System.Text.Json;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using InsightaAI.LLM.OpenAI;
using InsightaAI.LLM.Gemini;
using Xunit;

namespace InsightaAI.LLM.Tests;

public class ReasoningOffPolicyTests
{
    [Theory]
    [InlineData("o3-mini", ReasoningOffMode.Unsupported)]
    [InlineData("gpt-5", ReasoningOffMode.Unsupported)]
    [InlineData("gpt-5.2-pro", ReasoningOffMode.Unknown)]
    [InlineData("gpt-5.2-custom", ReasoningOffMode.Unknown)]
    [InlineData("ep-custom", ReasoningOffMode.Unknown)]
    public async Task UnsupportedOrUnknown_DoesNotEmitNone(string model, ReasoningOffMode expected)
    {
        foreach (IProviderAdapter adapter in new IProviderAdapter[] { new OpenAIAdapter(), new OpenAIResponseAdapter() })
        {
            using var http = adapter.CreateRequest(Request(model), new ProviderConfig { ApiKey = "test" }, false);
            Assert.True(http.Options.TryGetValue(ReasoningOffPolicy.ResolutionKey, out var mode));
            Assert.Equal(expected, mode);
            var body = JsonSerializer.Deserialize<JsonElement>(await http.Content!.ReadAsStringAsync());
            Assert.False(body.TryGetProperty("reasoning", out _));
            Assert.False(body.TryGetProperty("reasoning_effort", out _));
        }
    }

    [Fact]
    public async Task GeminiPro_DoesNotEmitZeroBudget()
    {
        using var http = new GeminiAdapter().CreateRequest(Request("gemini-2.5-pro"), new ProviderConfig { ApiKey = "test" }, false);
        var body = JsonSerializer.Deserialize<JsonElement>(await http.Content!.ReadAsStringAsync());
        Assert.False(body.TryGetProperty("generationConfig", out _));
    }

    [Fact]
    public void OverrideRejectsIncompatibleProtocol()
    {
        var request = Request("deployment") with { Reasoning = ReasoningConfig.Off() with { OffMode = ReasoningOffMode.EffortNone } };
        Assert.Throws<InvalidOperationException>(() => new GeminiAdapter().CreateRequest(request, new ProviderConfig { ApiKey = "test" }, false));
    }

    [Fact]
    public async Task DeploymentOverride_EmitsExplicitField()
    {
        var request = Request("ep-custom") with { Reasoning = ReasoningConfig.Off() with { OffMode = ReasoningOffMode.ThinkingDisabled } };
        using var http = new OpenAIAdapter().CreateRequest(request, new ProviderConfig { ApiKey = "test" }, true);
        var body = JsonSerializer.Deserialize<JsonElement>(await http.Content!.ReadAsStringAsync());
        Assert.Equal("disabled", body.GetProperty("thinking").GetProperty("type").GetString());
    }

    private static LlmRequest Request(string model) => new() { Model = model, Messages = [Message.FromUser("test")], Reasoning = ReasoningConfig.Off() };
}
