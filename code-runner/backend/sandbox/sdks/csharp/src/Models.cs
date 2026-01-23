using System.Text.Json.Serialization;

namespace CodeRunner.SandboxSdk.Models;

/// <summary>
/// Represents a running sandbox.
/// </summary>
public class Sandbox
{
    [JsonPropertyName("sandbox_id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("paths")]
    public SandboxPaths Paths { get; set; } = new();

    [JsonPropertyName("runtime")]
    public SandboxRuntime Runtime { get; set; } = new();

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;
}

public class SandboxPaths
{
    [JsonPropertyName("workspace")]
    public string Workspace { get; set; } = string.Empty;
}

public class SandboxRuntime
{
    [JsonPropertyName("image")]
    public string Image { get; set; } = string.Empty;

    [JsonPropertyName("resolved_from")]
    public string ResolvedFrom { get; set; } = string.Empty;
}

/// <summary>
/// Detailed sandbox information.
/// </summary>
public class SandboxDetail
{
    [JsonPropertyName("sandbox_id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("template")]
    public string Template { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("paths")]
    public SandboxPaths Paths { get; set; } = new();

    [JsonPropertyName("runtime")]
    public SandboxRuntime Runtime { get; set; } = new();

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("expires_at")]
    public string? ExpiresAt { get; set; }
}

/// <summary>
/// Sandbox environment information.
/// </summary>
public class SandboxEnvironment
{
    [JsonPropertyName("os")]
    public string Os { get; set; } = string.Empty;

    [JsonPropertyName("arch")]
    public string Arch { get; set; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = new();

    [JsonPropertyName("paths")]
    public Dictionary<string, string> Paths { get; set; } = new();
}

/// <summary>
/// Result of executing a command.
/// </summary>
public class ExecResult
{
    [JsonPropertyName("execution_id")]
    public string ExecutionId { get; set; } = string.Empty;

    [JsonPropertyName("exit_code")]
    public int ExitCode { get; set; }

    [JsonPropertyName("stdout")]
    public string Stdout { get; set; } = string.Empty;

    [JsonPropertyName("stderr")]
    public string Stderr { get; set; } = string.Empty;

    [JsonPropertyName("duration_ms")]
    public double DurationMs { get; set; }

    [JsonPropertyName("files_changed")]
    public List<string> FilesChanged { get; set; } = new();

    public bool Success => ExitCode == 0;
}

/// <summary>
/// A file or directory in the sandbox.
/// </summary>
public class FileItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("is_dir")]
    public bool IsDir { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }
}

/// <summary>
/// File content response.
/// </summary>
public class FileContent
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public int Size { get; set; }
}

/// <summary>
/// Export result.
/// </summary>
public class ExportResult
{
    [JsonPropertyName("artifact_id")]
    public string ArtifactId { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = string.Empty;
}

/// <summary>
/// Sandbox template definition.
/// </summary>
public class Template
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = new();

    [JsonPropertyName("defaults")]
    public Dictionary<string, string> Defaults { get; set; } = new();

    [JsonPropertyName("constraints")]
    public Dictionary<string, object> Constraints { get; set; } = new();
}
