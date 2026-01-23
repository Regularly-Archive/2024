using System.Net.Http.Json;
using System.Text.Json;
using CodeRunner.SandboxSdk.Models;

namespace CodeRunner.SandboxSdk;

/// <summary>
/// Client for the Code Runner Sandbox Runtime API.
/// </summary>
public class SandboxClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private bool _disposed;

    public SandboxClient(string baseUrl = "http://localhost:8002")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(300)
        };
    }

    // ============ Templates ============

    /// <summary>
    /// List all available templates.
    /// </summary>
    public async Task<List<Template>> ListTemplatesAsync()
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/sandbox/templates");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TemplatesResponse>();
        return result?.Templates ?? new List<Template>();
    }

    /// <summary>
    /// Get a specific template.
    /// </summary>
    public async Task<Template> GetTemplateAsync(string templateId)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/sandbox/templates/{templateId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Template>()
               ?? throw new InvalidOperationException("Failed to deserialize template");
    }

    // ============ Sandbox Lifecycle ============

    /// <summary>
    /// Create a new sandbox.
    /// Note: Resource limits are defined by the template.
    /// </summary>
    public async Task<Sandbox> CreateSandboxAsync(
        string template,
        string? workspaceFiles = null)
    {
        var requestBody = new Dictionary<string, object> { ["template"] = template };
        if (workspaceFiles != null)
        {
            requestBody["workspace"] = new { files = workspaceFiles };
        }

        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}/api/sandbox/sandboxes",
            requestBody,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }
        );
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Sandbox>()
               ?? throw new InvalidOperationException("Failed to deserialize sandbox");
    }

    /// <summary>
    /// Get sandbox details.
    /// </summary>
    public async Task<SandboxDetail> GetSandboxAsync(string sandboxId)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/sandbox/sandboxes/{sandboxId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SandboxDetail>()
               ?? throw new InvalidOperationException("Failed to deserialize sandbox detail");
    }

    /// <summary>
    /// List all running sandboxes.
    /// </summary>
    public async Task<List<Sandbox>> ListSandboxesAsync()
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/sandbox/sandboxes");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<Sandbox>>()
               ?? new List<Sandbox>();
    }

    /// <summary>
    /// Destroy a sandbox.
    /// </summary>
    public async Task DestroyAsync(string sandboxId, string? exportPath = null)
    {
        var url = $"{_baseUrl}/api/sandbox/sandboxes/{sandboxId}";
        if (exportPath != null)
        {
            url += $"?export={Uri.EscapeDataString(exportPath)}";
        }
        var response = await _httpClient.DeleteAsync(url);
        response.EnsureSuccessStatusCode();
    }

    // ============ Environment ============

    /// <summary>
    /// Get sandbox environment information.
    /// </summary>
    public async Task<SandboxEnvironment> GetEnvironmentAsync(string sandboxId)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/sandbox/sandboxes/{sandboxId}/env");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SandboxEnvironment>()
               ?? throw new InvalidOperationException("Failed to deserialize environment");
    }

    // ============ Execution ============

    /// <summary>
    /// Execute a command in the sandbox.
    /// </summary>
    public async Task<ExecResult> ExecAsync(
        string sandboxId,
        string cmd,
        string? cwd = null,
        Dictionary<string, string>? env = null,
        int? timeout = null)
    {
        var requestBody = new Dictionary<string, object> { ["cmd"] = cmd };
        if (cwd != null) requestBody["cwd"] = cwd;
        if (env != null) requestBody["env"] = env;
        if (timeout != null) requestBody["timeout"] = timeout;

        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}/api/sandbox/sandboxes/{sandboxId}/exec",
            requestBody,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }
        );
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ExecResult>()
               ?? throw new InvalidOperationException("Failed to deserialize exec result");
    }

    /// <summary>
    /// Execute a command and throw if it fails.
    /// </summary>
    public async Task<ExecResult> ExecAndCheckAsync(
        string sandboxId,
        string cmd,
        string? cwd = null,
        Dictionary<string, string>? env = null,
        int? timeout = null)
    {
        var result = await ExecAsync(sandboxId, cmd, cwd, env, timeout);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Command failed with exit code {result.ExitCode}:\n{result.Stderr}");
        }
        return result;
    }

    // ============ File Operations ============

    /// <summary>
    /// Write file to sandbox.
    /// </summary>
    public async Task WriteFileAsync(string sandboxId, string path, string content)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}/api/sandbox/sandboxes/{sandboxId}/write",
            new { path, content },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }
        );
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Read file from sandbox.
    /// </summary>
    public async Task<FileContent> ReadFileAsync(string sandboxId, string path)
    {
        var response = await _httpClient.GetAsync(
            $"{_baseUrl}/api/sandbox/sandboxes/{sandboxId}/file?path={Uri.EscapeDataString(path)}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FileContent>()
               ?? throw new InvalidOperationException("Failed to deserialize file content");
    }

    /// <summary>
    /// List files in sandbox.
    /// </summary>
    public async Task<List<FileItem>> ListFilesAsync(string sandboxId, string path = ".")
    {
        var response = await _httpClient.GetAsync(
            $"{_baseUrl}/api/sandbox/sandboxes/{sandboxId}/files?path={Uri.EscapeDataString(path)}");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<FileListResponse>();
        return result?.Items ?? new List<FileItem>();
    }

    // ============ Workspace Operations ============

    /// <summary>
    /// Export files from sandbox as artifact.
    /// </summary>
    public async Task<ExportResult> ExportAsync(string sandboxId, string path = ".")
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}/api/sandbox/sandboxes/{sandboxId}/export",
            new { path, as_artifact = true },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }
        );
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ExportResult>()
               ?? throw new InvalidOperationException("Failed to deserialize export result");
    }

    /// <summary>
    /// Upload local files to sandbox workspace.
    /// </summary>
    public async Task UploadWorkspaceAsync(
        string sandboxId,
        string sourcePath,
        bool clearFirst = false)
    {
        var url = $"{_baseUrl}/api/sandbox/sandboxes/{sandboxId}/upload?clear_first={clearFirst.ToString().ToLower()}";
        var response = await _httpClient.PostAsJsonAsync(
            url,
            new { source_path = sourcePath },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }
        );
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Sync multiple files to sandbox.
    /// </summary>
    public async Task<int> SyncFilesAsync(string sandboxId, Dictionary<string, string> files)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}/api/sandbox/sandboxes/{sandboxId}/sync",
            files,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }
        );
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SyncResponse>();
        return result?.Synced ?? 0;
    }

    // ============ Convenience Methods ============

    /// <summary>
    /// Execute a multi-line script.
    /// </summary>
    public async Task<ExecResult> RunScriptAsync(
        string sandboxId,
        string script,
        int? timeout = null)
    {
        const string scriptPath = "/tmp/script.sh";
        await WriteFileAsync(sandboxId, scriptPath, $"#!/bin/bash\nset -e\n{script}");
        return await ExecAndCheckAsync(sandboxId, $"bash {scriptPath}", timeout: timeout);
    }

    // ============ Dispose ============

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
    }

    // Helper classes for deserialization
    private class TemplatesResponse { public List<Template> Templates { get; set; } = new(); }
    private class FileListResponse { public List<FileItem> Items { get; set; } = new(); }
    private class SyncResponse { public int Synced { get; set; } }
}
