using System.Text.Json;
using System.Text.Json.Serialization;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Cli.Tests;

public class ReasoningConfigurationSerializationTests
{
    [Fact]
    public void Reasoning_Capability_Rejects_Unknown_Fields()
    {
        const string json = """
        {
          "model_id": "custom-model",
          "reasoning": {
            "strategy": "none",
            "supported": ["default", "off"],
            "mappings": {
              "off": {
                "control": "off",
                "off-mode": "thinkingDisabled"
              }
            }
          }
        }
        """;

        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ModelEntry>(json, CreateOptions()));

        Assert.Contains("off-mode", exception.Message);
        Assert.Contains("off-mode", exception.Path);
    }

    [Fact]
    public void Reasoning_Capability_Preserves_Canonical_Fields_When_Round_Tripped()
    {
        const string json = """
        {
          "model_id": "custom-model",
          "reasoning": {
            "strategy": "none",
            "supported": ["default", "off"],
            "mappings": {
              "off": {
                "control": "off",
                "offMode": "thinkingDisabled"
              }
            }
          }
        }
        """;

        var model = JsonSerializer.Deserialize<ModelEntry>(json, CreateOptions());
        var serialized = JsonSerializer.Serialize(model, CreateOptions());

        Assert.NotNull(model?.Reasoning);
        Assert.Equal(ReasoningOffMode.ThinkingDisabled,
            model.Reasoning.Mappings[ReasoningPreference.Off].OffMode);
        Assert.Contains("\"offMode\"", serialized);
        Assert.DoesNotContain("\"off-mode\"", serialized);
    }

    [Fact]
    public void Packaged_Catalog_Is_Loaded_Once_Per_Process()
    {
        var first = ModelReasoningCapabilityCatalog.LoadDefault();
        var second = ModelReasoningCapabilityCatalog.LoadDefault();

        Assert.Same(first, second);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
