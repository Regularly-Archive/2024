using InsightaAI.Agent.Abstractions;

namespace InsightaAI.Agent.Harness.Local;

/// <summary>
/// 本地执行环境的默认路径安全策略，拒绝写入敏感系统和凭据路径。
/// </summary>
public sealed class LocalPathValidator : IPathValidator
{
    private readonly string[] _dangerousPrefixes = GetDangerousPrefixes();

    private static readonly HashSet<string> DangerousFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bashrc", ".bash_profile", ".profile", ".zshrc",
        ".ssh", ".gnupg", ".aws", ".azure",
        "authorized_keys", "id_rsa", "id_ed25519"
    };

    public PathValidationResult Validate(string path, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return PathValidationResult.Dangerous("路径不能为空");

        try
        {
            var fullPath = Path.GetFullPath(path);

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                var workDir = Path.GetFullPath(workingDirectory);
                if (!fullPath.StartsWith(workDir, StringComparison.OrdinalIgnoreCase))
                {
                    return PathValidationResult.Dangerous(
                        $"路径 '{path}' 不在工作目录 '{workDir}' 内。只能写入工作目录内的文件。");
                }
            }

            foreach (var prefix in _dangerousPrefixes)
            {
                if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return PathValidationResult.Dangerous(
                        $"路径 '{path}' 指向敏感系统目录 '{prefix}'，拒绝写入。");
                }
            }

            var fileName = Path.GetFileName(fullPath);
            if (DangerousFileNames.Contains(fileName))
            {
                return PathValidationResult.Dangerous(
                    $"文件 '{fileName}' 是敏感系统文件，拒绝写入。");
            }

            return PathValidationResult.Safe(fullPath);
        }
        catch (Exception ex)
        {
            return PathValidationResult.Dangerous($"路径验证失败: {ex.Message}");
        }
    }

    private static string[] GetDangerousPrefixes()
    {
        var prefixes = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            prefixes.Add(Path.Combine(home, ".ssh"));
            prefixes.Add(Path.Combine(home, ".gnupg"));
            prefixes.Add(Path.Combine(home, ".aws"));
            prefixes.Add(Path.Combine(home, ".azure"));
            prefixes.Add(Path.Combine(home, ".kube"));
            prefixes.Add(Path.Combine(home, ".docker"));
        }

        if (OperatingSystem.IsWindows())
        {
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(winDir)) prefixes.Add(winDir);
            var sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrEmpty(sysDir)) prefixes.Add(sysDir);
        }
        else
        {
            prefixes.AddRange(["/etc", "/usr", "/var", "/boot", "/sbin", "/bin"]);
        }

        return prefixes.ToArray();
    }
}
