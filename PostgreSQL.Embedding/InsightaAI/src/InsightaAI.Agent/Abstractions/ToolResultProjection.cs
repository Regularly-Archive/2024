using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Abstractions;

public sealed record ToolResultRetentionPolicy
{
    public bool CanReplay { get; init; }
    public bool HasSideEffects { get; init; }
    public bool PreferPersistence { get; init; }
    public ToolResultRetentionLevel MinimumLevel { get; init; } = ToolResultRetentionLevel.Placeholder;
}

public sealed record ToolResultProjectionContext
{
    public required string ToolName { get; init; }
    public required string ToolCallId { get; init; }
    public required int OriginalLength { get; init; }
    public required Lazy<int> OriginalLineCount { get; init; }
    public ToolResultArtifactInfo? Artifact { get; init; }
}

public sealed record ToolResultProjection
{
    public required ContentBlock[] Content { get; init; }
    public required ToolResultRetentionLevel Level { get; init; }
}

/// <summary>工具只定义结果保留什么语义，不执行持久化或消息删除。</summary>
public interface IToolResultProjector
{
    ToolResultRetentionPolicy RetentionPolicy { get; }
    ToolResultProjection CreatePreview(ToolResult result, ToolResultProjectionContext context);
    ToolResultProjection CreatePlaceholder(ToolResultProjectionContext context);
}
