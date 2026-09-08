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

/// <summary>Conservative exact-model defaults; never infer support from a family prefix.</summary>
public static class ReasoningOffPolicy
{
    public static readonly HttpRequestOptionsKey<ReasoningOffMode> ResolutionKey =
        new("Insighta.ReasoningOffResolution");

    public static ReasoningOffMode Resolve(string adapter, LlmRequest request)
    {
        var mode = request.Reasoning?.OffMode ?? GetDefault(adapter, request.Model.ToLowerInvariant());
        var valid = mode is ReasoningOffMode.Unknown or ReasoningOffMode.Unsupported || adapter switch
        {
            "openai" => mode is ReasoningOffMode.EffortNone or ReasoningOffMode.ThinkingDisabled or ReasoningOffMode.EnableThinkingFalse,
            "openai-response" => mode == ReasoningOffMode.EffortNone,
            "anthropic" => mode == ReasoningOffMode.ThinkingDisabled,
            "gemini" => mode == ReasoningOffMode.ThinkingBudgetZero,
            _ => false
        };
        if (!valid) throw new InvalidOperationException($"Off mode {mode} is incompatible with adapter {adapter}.");
        return mode;
    }

    private static ReasoningOffMode GetDefault(string adapter, string model) => (adapter, model) switch
    {
        ("openai" or "openai-response", "gpt-5.1" or "gpt-5.1-2025-11-13" or "gpt-5.2" or "gpt-5.2-2025-12-11") => ReasoningOffMode.EffortNone,
        ("openai" or "openai-response", "o1" or "o3" or "o3-mini" or "o4-mini" or "gpt-5" or "gpt-5-mini" or "gpt-5-nano") => ReasoningOffMode.Unsupported,
        ("gemini", "gemini-2.5-flash" or "gemini-2.5-flash-lite") => ReasoningOffMode.ThinkingBudgetZero,
        ("gemini", "gemini-2.5-pro") => ReasoningOffMode.Unsupported,
        ("anthropic", "claude-sonnet-4-5" or "claude-sonnet-4-20250514") => ReasoningOffMode.ThinkingDisabled,
        _ => ReasoningOffMode.Unknown
    };

    public static void Record(HttpRequestMessage httpRequest, string adapter, LlmRequest request)
    {
        if (request.Reasoning?.Control != ReasoningControl.Off) return;
        var mode = Resolve(adapter, request);
        httpRequest.Options.Set(ResolutionKey, mode);
        // Records request translation, not proof of remote model behavior. No prompt or secrets.
        System.Diagnostics.Activity.Current?.SetTag("insighta.reasoning.off_resolution", mode.ToString());
    }
}
