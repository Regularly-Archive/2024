using System.Diagnostics;
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
    [InlineData("o3-mini")]
    [InlineData("gpt-5")]
    [InlineData("gpt-5.2")]
    [InlineData("ep-custom")]
    public async Task Unresolved_Off_DoesNotEmitControlFields(string model)
    {
        foreach (IProviderAdapter adapter in new IProviderAdapter[] { new OpenAIAdapter(), new OpenAIResponseAdapter() })
        {
            using var http = adapter.CreateRequest(Request(model), new ProviderConfig { ApiKey = "test" }, false);
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
        var request = LlmRequest.WithResolvedReasoning(Request("deployment"), ReasoningConfig.Off() with { OffMode = ReasoningOffMode.EffortNone });
        Assert.Throws<InvalidOperationException>(() => new GeminiAdapter().CreateRequest(request, new ProviderConfig { ApiKey = "test" }, false));
    }

    [Fact]
    public async Task DeploymentOverride_EmitsExplicitField()
    {
        var request = LlmRequest.WithResolvedReasoning(Request("ep-custom"), ReasoningConfig.Off() with { OffMode = ReasoningOffMode.ThinkingDisabled });
        using var http = new OpenAIAdapter().CreateRequest(request, new ProviderConfig { ApiKey = "test" }, true);
        var body = JsonSerializer.Deserialize<JsonElement>(await http.Content!.ReadAsStringAsync());
        Assert.Equal("disabled", body.GetProperty("thinking").GetProperty("type").GetString());
    }

    [Fact]
    public void Resolution_Is_Recorded_On_The_Current_Activity()
    {
        using var activity = new Activity("reasoning-off-test").Start();

        ReasoningOffPolicy.RecordTelemetry("openai", Request("custom-model"));

        Assert.Equal("Unknown", activity.GetTagItem("insighta.reasoning.off_resolution"));
    }

    private static LlmRequest Request(string model) => LlmRequest.WithResolvedReasoning(
        new LlmRequest { Model = model, Messages = [Message.FromUser("test")] },
        ReasoningConfig.Off());
}
