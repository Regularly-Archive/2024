namespace InsightaAI.Agent.Abstractions;

/// <summary>
/// Result of Intercept method on IToolExecutor.
/// Carries metadata about whether the result was intercepted at execution time.
/// </summary>
public sealed record InterceptionResult
{
    /// <summary>Intercepted tool result (may be truncated or with preview)</summary>
    public ToolResult Result { get; init; }
    
    /// <summary>Whether this result was intercepted at execution time</summary>
    public bool ToolResultIntercepted { get; init; }
    
    /// <summary>Path to persisted file (if applicable)</summary>
    public string? PersistedPath { get; init; }
    
    /// <summary>Original result length before interception</summary>
    public int OriginalLength { get; init; }
    
    /// <summary>Create a new InterceptionResult</summary>
    public InterceptionResult(
        ToolResult result, 
        bool toolResultIntercepted, 
        string? persistedPath = null, 
        int originalLength = 0)
    {
        Result = result;
        ToolResultIntercepted = toolResultIntercepted;
        PersistedPath = persistedPath;
        OriginalLength = originalLength;
    }
    
    /// <summary>Create a non-intercepted result (pass-through)</summary>
    public static InterceptionResult NotIntercepted(ToolResult result) => 
        new(result, toolResultIntercepted: false);
}
