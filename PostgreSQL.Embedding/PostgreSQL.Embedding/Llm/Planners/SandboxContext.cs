using Amazon.Runtime.Internal.Transform;
using System.IO;

namespace PostgreSQL.Embedding.Llm.Planners;

internal class SandboxContext : ISandboxContext
{
    public string BaseDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".insighta",
        "apps"
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
        SessionDir = Path.Combine(AppDir, "conversations", conversationId);
        ArtifactsDir = Path.Combine(SessionDir, "artifacts");
        RunDir = Path.Combine(SessionDir, "runs", runId);
        SkillsDir = Path.Combine(AppDir, ".skills");

        EnsureDirectoriesExisit([AppDir, SessionDir, RunDir, ArtifactsDir, SkillsDir]);

        _workingDirInSandbox = workDir;


        foreach (var volume in GetVolumeMappings())
        {
            if (volume.Key.EndsWith(".md") && !File.Exists(volume.Key))
            {
                File.WriteAllText(volume.Key, string.Empty);
            }
            else if (!volume.Key.EndsWith(".md") && !Directory.Exists(volume.Key))
            {
                {
                    Directory.CreateDirectory(volume.Key);
                }
            }
        }
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
            { SkillsDir, "/sandbox/.skills" },
            { ArtifactsDir, "/sandbox/artifacts" },
            //{ Path.Combine(SessionDir, "MEMORY.md"), "/sandbox/MEMORY.md" },
            //{ Path.Combine(AppDir, "SOUL.md"), "/sandbox/SOUL.md" },
            //{ Path.Combine(RunDir, "SHORT_TERM.md"), "/sandbox/SHORT_TERM.md" }
        };
    }

    private void EnsureDirectoriesExisit(IEnumerable<string> dirs)
    {
        foreach (var dir in dirs)
        {
            if (Directory.Exists(dir)) continue;
            Directory.CreateDirectory(dir);
        }
    }
}
