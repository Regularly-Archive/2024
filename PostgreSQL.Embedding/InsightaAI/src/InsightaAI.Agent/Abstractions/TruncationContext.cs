using InsightaAI.Agent.Context;

namespace InsightaAI.Agent.Abstractions;

/// <summary>
/// Context for tool result processing decisions.
/// Provides information about the result size, context utilization, and persistence directory.
/// </summary>
public sealed record TruncationContext
{
    /// <summary>Original character count of the result</summary>
    public int OriginalLength { get; init; }
    
    /// <summary>Original line count of the result (lazy computed to avoid unnecessary array allocation)</summary>
    public Lazy<int> OriginalLineCount { get; init; }
    
    /// <summary>Current context utilization ratio (0.0 ~ 1.0)</summary>
    public double UtilizationRatio { get; init; }
    
    /// <summary>Context budget configuration (read-only)</summary>
    public ContextBudget? Budget { get; init; }
    
    /// <summary>Directory for persisting large tool results</summary>
    public string ToolResultDirectory { get; init; }
    
    /// <summary>Tool name (for file naming)</summary>
    public string ToolName { get; init; }
    
    /// <summary>Tool call ID (for file naming uniqueness)</summary>
    public string ToolCallId { get; init; }
    
    /// <summary>Force truncation regardless of thresholds (for emergency)</summary>
    public bool ForceTruncate { get; init; }
    
    /// <summary>Create a new TruncationContext</summary>
    public TruncationContext(
        int originalLength,
        Lazy<int> originalLineCount,
        double utilizationRatio,
        ContextBudget? budget,
        string toolResultDirectory,
        string toolName,
        string toolCallId,
        bool forceTruncate = false)
    {
        OriginalLength = originalLength;
        OriginalLineCount = originalLineCount;
        UtilizationRatio = utilizationRatio;
        Budget = budget;
        ToolResultDirectory = toolResultDirectory;
        ToolName = toolName;
        ToolCallId = toolCallId;
        ForceTruncate = forceTruncate;
    }
}
