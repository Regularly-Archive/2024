namespace InsightaAI.Orchestrator.Scheduling;

/// <summary>
/// DAG 验证结果
/// </summary>
public sealed record ValidationResult
{
    /// <summary>是否有效</summary>
    public bool IsValid { get; init; }

    /// <summary>错误信息</summary>
    public string[] Errors { get; init; } = [];

    /// <summary>警告信息</summary>
    public string[] Warnings { get; init; } = [];

    /// <summary>创建成功结果</summary>
    public static ValidationResult Success() => new() { IsValid = true };

    /// <summary>创建失败结果</summary>
    public static ValidationResult Failure(params string[] errors) =>
        new() { IsValid = false, Errors = errors };
}
