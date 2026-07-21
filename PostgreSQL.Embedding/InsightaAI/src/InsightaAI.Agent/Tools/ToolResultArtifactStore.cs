using System.Text;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools;

public interface IToolResultArtifactStore
{
    Task<ToolResultArtifactInfo> SaveAsync(string sessionId, string toolName, string toolCallId,
        string content, CancellationToken cancellationToken = default);
}

/// <summary>统一保存原始完整工具结果。</summary>
public sealed class ToolResultArtifactStore : IToolResultArtifactStore
{
    private readonly IFileSystem _fileSystem;
    private readonly string _basePath;

    public ToolResultArtifactStore(IFileSystem fileSystem, string? basePath = null)
    {
        _fileSystem = fileSystem;
        _basePath = basePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".insighta", "sessions");
    }

    public async Task<ToolResultArtifactInfo> SaveAsync(string sessionId, string toolName,
        string toolCallId, string content, CancellationToken cancellationToken = default)
    {
        var artifactId = Guid.NewGuid().ToString("N")[..12];
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeToolName = string.Concat(toolName.Select(c => invalidChars.Contains(c) ? '_' : c));
        var safeCallId = string.Concat(toolCallId.Select(c => invalidChars.Contains(c) ? '_' : c));
        var fileName = $"{safeToolName}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{safeCallId}_{artifactId}.txt";
        var path = Path.Combine(_basePath, sessionId, "tool_results", fileName);

        await _fileSystem.WriteFileAsync(path, content, Encoding.UTF8, cancellationToken);
        return new ToolResultArtifactInfo
        {
            Id = artifactId,
            Path = path,
            ByteSize = Encoding.UTF8.GetByteCount(content)
        };
    }
}
