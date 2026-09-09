using InsightaAI.Agent.Cli.Models;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>Resolves one product preference against one configured model deployment.</summary>
public sealed class ModelReasoningResolver
{
    private readonly string _modelReference;
    private readonly ModelReasoningCapability? _capability;

    public ModelReasoningResolver(
        string modelReference,
        string adapter,
        ModelEntry model,
        ModelReasoningCapabilityCatalog catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(catalog);

        _modelReference = modelReference;
        _capability = model.Reasoning ?? catalog.Find(adapter, model.ModelId);
        if (_capability != null)
            Validate(_capability, modelReference, adapter);
    }

    /// <summary>Creates a resolver from an already selected capability; useful for non-file-backed hosts and tests.</summary>
    public ModelReasoningResolver(string modelReference, ModelReasoningCapability? capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelReference);
        _modelReference = modelReference;
        _capability = capability;
        if (_capability != null)
            Validate(_capability, modelReference, adapter: null);
    }

    public ReasoningConfig? Resolve(ReasoningPreference preference)
    {
        if (preference == ReasoningPreference.Default)
            return null;

        if (_capability == null || !_capability.Supported.Contains(preference))
        {
            throw new InvalidOperationException(
                $"Model '{_modelReference}' does not declare support for reasoning preference '{preference.ToString().ToLowerInvariant()}'. " +
                "Only 'default' is available until the model capability is configured.");
        }

        if (!_capability.Mappings.TryGetValue(preference, out var mapping))
            throw new InvalidOperationException(
                $"Model '{_modelReference}' supports reasoning preference '{preference.ToString().ToLowerInvariant()}' " +
                "but does not define its native mapping.");

        mapping.Validate();
        return mapping;
    }

    public bool Supports(ReasoningPreference preference) =>
        preference == ReasoningPreference.Default ||
        _capability is { } capability &&
        capability.Supported.Contains(preference) &&
        capability.Mappings.ContainsKey(preference);

    private static void Validate(ModelReasoningCapability capability, string modelReference, string? adapter)
    {
        if (!capability.Supported.Contains(ReasoningPreference.Default))
            throw new InvalidOperationException($"Model '{modelReference}' reasoning capability must include 'default'.");

        foreach (var preference in capability.Supported.Where(x => x != ReasoningPreference.Default))
        {
            if (!capability.Mappings.ContainsKey(preference))
                throw new InvalidOperationException($"Model '{modelReference}' supports preference '{preference}' but does not map it.");
        }

        foreach (var (preference, mapping) in capability.Mappings)
        {
            if (preference == ReasoningPreference.Default)
                throw new InvalidOperationException($"Model '{modelReference}' must not map 'default'; it always omits control fields.");
            if (!capability.Supported.Contains(preference))
                throw new InvalidOperationException($"Model '{modelReference}' maps unsupported preference '{preference}'.");

            mapping.Validate();
            if (preference == ReasoningPreference.Off && mapping.Control != ReasoningControl.Off)
                throw new InvalidOperationException($"Model '{modelReference}' must map 'off' to ReasoningControl.Off.");
            if (preference != ReasoningPreference.Off && mapping.Control == ReasoningControl.Off)
                throw new InvalidOperationException($"Model '{modelReference}' may only use ReasoningControl.Off for 'off'.");
            if (mapping.Control == ReasoningControl.ProviderDefault)
                throw new InvalidOperationException($"Model '{modelReference}' must not map an explicit preference to ProviderDefault.");
            if (preference == ReasoningPreference.Off && adapter != null)
                ReasoningOffPolicy.ValidateCompatibility(adapter, mapping.OffMode ?? ReasoningOffMode.Unknown);
        }
    }
}
