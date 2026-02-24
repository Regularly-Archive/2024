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
    public string SkillsDir { get; }

    private string _workingDirInSandbox;

    internal SandboxContext(long appId, string conversationId, string runId, string workDir)
    {
        AppDir = Path.Combine(BaseDir, appId.ToString());
        SessionDir = Path.Combine(AppDir, conversationId);
        RunDir = Path.Combine(SessionDir, "runs", runId);
        ArtifactsDir = Path.Combine(RunDir, "artifacts");
        SkillsDir = Path.Combine(AppDir, ".skills");
        _workingDirInSandbox = workDir;
    }

    public string ToLocalPath(string sandboxPath)
    {
        if (string.IsNullOrWhiteSpace(sandboxPath))
            throw new ArgumentNullException(nameof(sandboxPath));

        if (sandboxPath.StartsWith(_workingDirInSandbox))
            sandboxPath = sandboxPath.Replace(_workingDirInSandbox, "").TrimStart('/');

        var fullPath = Path.GetFullPath(Path.Combine(RunDir, sandboxPath));

        if (!IsPathAllowed(fullPath))
            throw new UnauthorizedAccessException($"Access denied: Cannot access path outside sandbox ({sandboxPath})");

        return fullPath;
    }

    public bool IsPathAllowed(string localFullPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(localFullPath);
            return fullPath.StartsWith(RunDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private string ToSandboxPath(string basePath, string fullPath)
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

    /// <summary>
    /// 将本地完整路径转换为沙箱内的 Linux 风格路径（默认以 RunDir 为基准）
    /// </summary>
    public string ToSandboxPath(string localFullPath)
    {
        return ToSandboxPath(RunDir, localFullPath);
    }

    /// <summary>
    /// 获取 Docker 卷映射字典（本地路径 -> 容器内路径）
    /// </summary>
    public Dictionary<string, string> GetVolumeMappings()
    {
        return new Dictionary<string, string>
        {
            { RunDir, "/sandbox" },
            { SkillsDir, "/sandbox/.skills" },
            { Path.Combine(RunDir, "MEMORY.md"), "/sandbox/MEMORY.md" },
            { Path.Combine(AppDir, "SOUL.md"), "/sandbox/SOUL.md" }
        };
    }
}
