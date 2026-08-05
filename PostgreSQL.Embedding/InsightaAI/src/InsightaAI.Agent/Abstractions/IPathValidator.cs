namespace InsightaAI.Agent.Abstractions;

/// <summary>
/// 验证工具可写入的路径是否符合当前执行环境的安全策略。
/// </summary>
public interface IPathValidator
{
    PathValidationResult Validate(string path, string? workingDirectory = null);
}

/// <summary>
/// 路径安全验证结果。
/// </summary>
public sealed record PathValidationResult(
    bool IsSafe,
    string? ResolvedPath,
    string? ErrorMessage)
{
    public static PathValidationResult Safe(string resolvedPath) => new(true, resolvedPath, null);

    public static PathValidationResult Dangerous(string errorMessage) => new(false, null, errorMessage);
}
