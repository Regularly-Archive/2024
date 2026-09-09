using System.Text.Json;
using System.Text.Json.Serialization;

namespace InsightaAI.Agent.Cli.Models;

/// <summary>Packaged, exact-model reasoning capability records.</summary>
public sealed class ModelReasoningCapabilityCatalog
{
    private const string AssetRelativePath = "Assets/model-reasoning-capabilities.json";
    private readonly Dictionary<CapabilityKey, ModelReasoningCapability> _capabilities;

    private ModelReasoningCapabilityCatalog(Dictionary<CapabilityKey, ModelReasoningCapability> capabilities)
    {
        _capabilities = capabilities;
    }

    public ModelReasoningCapability? Find(string adapter, string modelId) =>
        _capabilities.TryGetValue(new CapabilityKey(adapter, modelId), out var capability)
            ? capability
            : null;

    public static ModelReasoningCapabilityCatalog LoadDefault()
    {
        var path = Path.Combine(AppContext.BaseDirectory, AssetRelativePath);
        if (!File.Exists(path))
            throw new InvalidOperationException($"Packaged model capability catalog was not found at '{path}'.");

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var entries = JsonSerializer.Deserialize<ModelReasoningCapabilityEntry[]>(json, options)
            ?? throw new InvalidOperationException("Packaged model capability catalog is empty.");

        var capabilities = new Dictionary<CapabilityKey, ModelReasoningCapability>();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Adapter) || string.IsNullOrWhiteSpace(entry.ModelId))
                throw new InvalidOperationException("Every model capability record requires adapter and model_id.");
            if (!capabilities.TryAdd(new CapabilityKey(entry.Adapter, entry.ModelId), entry.Capability))
                throw new InvalidOperationException($"Duplicate model capability record: {entry.Adapter}/{entry.ModelId}.");
        }

        return new ModelReasoningCapabilityCatalog(capabilities);
    }

    private sealed class ModelReasoningCapabilityEntry
    {
        [JsonPropertyName("adapter")]
        public string Adapter { get; init; } = "";

        [JsonPropertyName("model_id")]
        public string ModelId { get; init; } = "";

        [JsonPropertyName("capability")]
        public ModelReasoningCapability Capability { get; init; } = new();
    }

    private readonly record struct CapabilityKey
    {
        public CapabilityKey(string adapter, string modelId)
        {
            Adapter = adapter.ToLowerInvariant();
            ModelId = modelId.ToLowerInvariant();
        }

        public string Adapter { get; }
        public string ModelId { get; }
    }
}
