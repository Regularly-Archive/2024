using System.Text.Json.Serialization;

namespace PostgreSQL.Embedding.Common.Streaming;

/// <summary>
/// Artifact event - sent when a new artifact is created
/// </summary>
public class ArtifactEvent : ISseEvent
{
    [JsonPropertyName("type")]
    public string Type => "artifact";

    [JsonPropertyName("artifact")]
    public ArtifactData Artifact { get; set; } = new();
}

public class ArtifactData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("access_url")]
    public string AccessUrl { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("can_preview")]
    public bool CanPreview { get; set; }

    [JsonPropertyName("can_download")]
    public bool CanDownload { get; set; } = true;

    [JsonPropertyName("file_size")]
    public long? FileSize { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
