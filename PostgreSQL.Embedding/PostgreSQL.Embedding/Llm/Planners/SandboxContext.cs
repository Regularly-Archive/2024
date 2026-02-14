using System.IO;

namespace PostgreSQL.Embedding.Llm.Planners;

internal class SandboxContext : ISandboxContext
{
    public string BaseDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".insighta"
    );

    public string AppDir { get; }
    public string SessionDir { get; }
    public string RunDir { get; }
    public string ArtifactsDir { get; }

    private string _workingDir;

    internal SandboxContext(long appId, string conversationId, string runId, string workDir)
    {
        AppDir = Path.Combine(BaseDir, appId.ToString());
        SessionDir = Path.Combine(AppDir, conversationId);
        RunDir = Path.Combine(SessionDir, "runs", runId);
        ArtifactsDir = Path.Combine(RunDir, "artifacts");
        _workingDir = workDir;
    }

    public string ResolvePath(string relativePath)
    {
        if (relativePath.StartsWith(_workingDir)) relativePath = relativePath.Replace(_workingDir, "").TrimStart(Path.PathSeparator);

        var fullPath = Path.GetFullPath(Path.Combine(SessionDir, relativePath));

        if (!IsPathAllowed(fullPath))
        {
            throw new UnauthorizedAccessException($"Path outside sandbox: {relativePath}");
        }

        return fullPath;
    }

    public bool IsPathAllowed(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(SessionDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public string ToLinuxStyleRelativePath(string basePath, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            throw new ArgumentNullException(nameof(basePath));

        if (string.IsNullOrWhiteSpace(fullPath))
            throw new ArgumentNullException(nameof(fullPath));

        var relativePath = Path.GetRelativePath(basePath, fullPath);

        relativePath = relativePath.Replace('\\', '/');

        if (!relativePath.StartsWith("/")) relativePath = "/" + relativePath;

        return relativePath;
    }

    public string FromLinuxStyleRelativePath(string basePath, string linuxPath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            throw new ArgumentNullException(nameof(basePath));

        if (string.IsNullOrWhiteSpace(linuxPath))
            throw new ArgumentNullException(nameof(linuxPath));

        var relativePath = linuxPath.TrimStart('/');

        relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);

        var combinedPath = Path.GetFullPath(Path.Combine(basePath, relativePath))
            .TrimEnd(Path.DirectorySeparatorChar);

        var normalizedBasePath = Path.GetFullPath(basePath)
            .TrimEnd(Path.DirectorySeparatorChar);

        if (!combinedPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"A path escape detected: {linuxPath}");

        return combinedPath;
    }
}
