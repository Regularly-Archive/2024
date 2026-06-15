namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// 路径验证器 - 防止写入敏感系统路径
/// </summary>
internal static class PathValidator
{
    /// <summary>
    /// 危险路径前缀（规范化后）
    /// </summary>
    private static readonly string[] DangerousPrefixes = GetDangerousPrefixes();

    /// <summary>
    /// 危险文件名
    /// </summary>
    private static readonly HashSet<string> DangerousFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bashrc", ".bash_profile", ".profile", ".zshrc",
        ".ssh", ".gnupg", ".aws", ".azure",
        "authorized_keys", "id_rsa", "id_ed25519"
    };

    /// <summary>
    /// 验证文件路径是否安全可写
    /// </summary>
    /// <param name="path">要验证的路径</param>
    /// <param name="workingDirectory">允许的工作目录（可选）</param>
    /// <returns>验证结果</returns>
    public static ValidationResult Validate(string path, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ValidationResult.Dangerous("路径不能为空");

        try
        {
            // 规范化路径
            var fullPath = Path.GetFullPath(path);

            // 检查是否在工作目录内（如果指定了工作目录）
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                var workDir = Path.GetFullPath(workingDirectory);
                if (!fullPath.StartsWith(workDir, StringComparison.OrdinalIgnoreCase))
                {
                    return ValidationResult.Dangerous(
                        $"路径 '{path}' 不在工作目录 '{workDir}' 内。只能写入工作目录内的文件。");
                }
            }

            // 检查危险路径前缀
            foreach (var prefix in DangerousPrefixes)
            {
                if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return ValidationResult.Dangerous(
                        $"路径 '{path}' 指向敏感系统目录 '{prefix}'，拒绝写入。");
                }
            }

            // 检查危险文件名
            var fileName = Path.GetFileName(fullPath);
            if (DangerousFileNames.Contains(fileName))
            {
                return ValidationResult.Dangerous(
                    $"文件 '{fileName}' 是敏感系统文件，拒绝写入。");
            }

            return ValidationResult.Safe(fullPath);
        }
        catch (Exception ex)
        {
            return ValidationResult.Dangerous($"路径验证失败: {ex.Message}");
        }
    }

    private static string[] GetDangerousPrefixes()
    {
        var prefixes = new List<string>();

        // 用户主目录下的敏感目录
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

        // Windows 系统目录
        if (OperatingSystem.IsWindows())
        {
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(winDir))
            {
                prefixes.Add(winDir);
            }
            var sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrEmpty(sysDir))
            {
                prefixes.Add(sysDir);
            }
        }
        else
        {
            // Unix/Linux 系统目录
            prefixes.Add("/etc");
            prefixes.Add("/usr");
            prefixes.Add("/var");
            prefixes.Add("/boot");
            prefixes.Add("/sbin");
            prefixes.Add("/bin");
        }

        return prefixes.ToArray();
    }

    internal readonly struct ValidationResult
    {
        public bool IsSafe { get; }
        public string? ResolvedPath { get; }
        public string? ErrorMessage { get; }

        private ValidationResult(bool isSafe, string? resolvedPath, string? errorMessage)
        {
            IsSafe = isSafe;
            ResolvedPath = resolvedPath;
            ErrorMessage = errorMessage;
        }

        public static ValidationResult Safe(string resolvedPath) => new(true, resolvedPath, null);
        public static ValidationResult Dangerous(string errorMessage) => new(false, null, errorMessage);
    }
}
