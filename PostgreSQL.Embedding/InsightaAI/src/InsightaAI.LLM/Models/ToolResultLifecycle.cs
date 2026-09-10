using System.Text.Json.Serialization;

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
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("contentType")]
    public string ContentType { get; init; } = "text/plain";

    [JsonPropertyName("byteSize")]
    public long ByteSize { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>随消息持久化的工具结果生命周期状态。</summary>
public sealed record ToolResultState
{
    [JsonPropertyName("retentionLevel")]
    public ToolResultRetentionLevel RetentionLevel { get; init; } = ToolResultRetentionLevel.Full;

    [JsonPropertyName("artifact")]
    public ToolResultArtifactInfo? Artifact { get; init; }

    [JsonPropertyName("originalLength")]
    public int OriginalLength { get; init; }

    [JsonPropertyName("canReplay")]
    public bool CanReplay { get; init; }

    [JsonPropertyName("hasSideEffects")]
    public bool HasSideEffects { get; init; }

    [JsonPropertyName("minimumLevel")]
    public ToolResultRetentionLevel MinimumLevel { get; init; } = ToolResultRetentionLevel.Placeholder;
}
