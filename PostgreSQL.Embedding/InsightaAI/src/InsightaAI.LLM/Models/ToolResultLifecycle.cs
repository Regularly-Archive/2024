namespace InsightaAI.LLM.Models;

/// <summary>工具结果在 LLM 上下文中的保留等级。</summary>
public enum ToolResultRetentionLevel
{
    Full = 0,
    Preview = 1,
    Placeholder = 2,
    Removed = 3
}

/// <summary>已持久化的原始工具结果引用。</summary>
public sealed record ToolResultArtifactInfo
{
    public required string Id { get; init; }
    public required string Path { get; init; }
    public string ContentType { get; init; } = "text/plain";
    public long ByteSize { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>随消息持久化的工具结果生命周期状态。</summary>
public sealed record ToolResultState
{
    public ToolResultRetentionLevel RetentionLevel { get; init; } = ToolResultRetentionLevel.Full;
    public ToolResultArtifactInfo? Artifact { get; init; }
    public int OriginalLength { get; init; }
    public bool CanReplay { get; init; }
    public bool HasSideEffects { get; init; }
    public ToolResultRetentionLevel MinimumLevel { get; init; } = ToolResultRetentionLevel.Placeholder;
}
