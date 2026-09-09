namespace InsightaAI.LLM.Models;

/// <summary>Wire control for a model/deployment. Unknown models keep provider defaults.</summary>
public enum ReasoningOffMode
{
    Unknown,
    Unsupported,
    EffortNone,
    ThinkingDisabled,
    EnableThinkingFalse,
    ThinkingBudgetZero
}

/// <summary>
/// Validates and records an already-resolved wire control. Model capability resolution belongs
/// to the host configuration layer; this class deliberately never infers support from a model name.
/// </summary>
public static class ReasoningOffPolicy
{
    public static readonly HttpRequestOptionsKey<ReasoningOffMode> ResolutionKey =
        new("Insighta.ReasoningOffResolution");

    public static ReasoningOffMode Resolve(string adapter, LlmRequest request)
    {
        var mode = request.Reasoning?.OffMode ?? ReasoningOffMode.Unknown;
        ValidateCompatibility(adapter, mode);
        return mode;
    }

    public static void ValidateCompatibility(string adapter, ReasoningOffMode mode)
    {
        var valid = mode is ReasoningOffMode.Unknown or ReasoningOffMode.Unsupported || adapter switch
        {
            "openai" => mode is ReasoningOffMode.EffortNone or ReasoningOffMode.ThinkingDisabled or ReasoningOffMode.EnableThinkingFalse,
            "openai-response" => mode == ReasoningOffMode.EffortNone,
            "anthropic" => mode == ReasoningOffMode.ThinkingDisabled,
            "gemini" => mode == ReasoningOffMode.ThinkingBudgetZero,
            _ => false
        };
        if (!valid) throw new InvalidOperationException($"Off mode {mode} is incompatible with adapter {adapter}.");
    }

    public static void Record(HttpRequestMessage httpRequest, string adapter, LlmRequest request)
    {
        if (request.Reasoning?.Control != ReasoningControl.Off) return;
        var mode = Resolve(adapter, request);
        httpRequest.Options.Set(ResolutionKey, mode);
        // Records request translation, not proof of remote model behavior. No prompt or secrets.
        System.Diagnostics.Activity.Current?.SetTag("insighta.reasoning.off_resolution", mode.ToString());
    }
}
