using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Cli.Services;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Cli.Tests;

public class ModelReasoningResolverTests
{
    [Fact]
    public void Packaged_Catalog_Resolves_Exact_Model_Record()
    {
        var catalog = ModelReasoningCapabilityCatalog.LoadDefault();
        var model = new ModelEntry { ModelId = "gpt-5.2" };
        var resolver = new ModelReasoningResolver("openai/gpt-5.2", "openai", model, catalog);

        var result = resolver.Resolve(ReasoningPreference.Off);

        Assert.NotNull(result);
        Assert.Equal(ReasoningOffMode.EffortNone, result.OffMode);
    }

    [Fact]
    public void Default_Always_Omits_Native_Control()
    {
        var resolver = new ModelReasoningResolver("provider/custom", capability: null);

        Assert.Null(resolver.Resolve(ReasoningPreference.Default));
    }

    [Fact]
    public void Unknown_Model_Rejects_Explicit_Preference()
    {
        var resolver = new ModelReasoningResolver("provider/custom", capability: null);

        var exception = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(ReasoningPreference.Off));
        Assert.Contains("Only 'default'", exception.Message);
    }

    [Fact]
    public void Declared_Off_Resolves_To_Exact_Native_Control()
    {
        var capability = new ModelReasoningCapability
        {
            Supported = [ReasoningPreference.Default, ReasoningPreference.Off],
            Mappings =
            {
                [ReasoningPreference.Off] = ReasoningConfig.Off() with { OffMode = ReasoningOffMode.EnableThinkingFalse }
            }
        };
        var resolver = new ModelReasoningResolver("qoder/qwen", capability);

        var result = resolver.Resolve(ReasoningPreference.Off);

        Assert.NotNull(result);
        Assert.Equal(ReasoningControl.Off, result.Control);
        Assert.Equal(ReasoningOffMode.EnableThinkingFalse, result.OffMode);
    }

    [Fact]
    public void Invalid_Capability_Requires_A_Mapping_For_Every_Supported_Preference()
    {
        var capability = new ModelReasoningCapability
        {
            Supported = [ReasoningPreference.Default, ReasoningPreference.Deep]
        };

        Assert.Throws<InvalidOperationException>(() => new ModelReasoningResolver("provider/model", capability));
    }

    [Fact]
    public void Configured_Off_Mode_Must_Match_Adapter()
    {
        var capability = new ModelReasoningCapability
        {
            Supported = [ReasoningPreference.Default, ReasoningPreference.Off],
            Mappings =
            {
                [ReasoningPreference.Off] = ReasoningConfig.Off() with { OffMode = ReasoningOffMode.ThinkingBudgetZero }
            }
        };
        var catalog = ModelReasoningCapabilityCatalog.LoadDefault();

        Assert.Throws<InvalidOperationException>(() =>
            new ModelReasoningResolver("openai/custom", "openai", new ModelEntry
            {
                ModelId = "custom",
                Reasoning = capability
            }, catalog));
    }

    [Fact]
    public async Task Internal_Auxiliary_Request_Can_Fall_Back_But_Explicit_Request_Cannot()
    {
        var middleware = new ModelReasoningMiddleware(
            new ModelReasoningResolver("provider/custom", capability: null));
        var request = new LlmRequest
        {
            Model = "custom",
            Messages = [Message.FromUser("summarize")],
            ReasoningPreference = ReasoningPreference.Off,
            AllowReasoningFallbackToDefault = true
        };

        var resolved = middleware.Invoke(request);

        Assert.Null(resolved.ReasoningPreference);
        Assert.Null(resolved.Reasoning);
        Assert.False(resolved.AllowReasoningFallbackToDefault);
        Assert.Throws<InvalidOperationException>(() => middleware.Invoke(request with
        {
            AllowReasoningFallbackToDefault = false
        }));
    }
}
