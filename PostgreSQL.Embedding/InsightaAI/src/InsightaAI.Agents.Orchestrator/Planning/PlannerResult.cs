namespace InsightaAI.Agents.Orchestrator.Planning;

/// <summary>
/// LLM 任务规划器的原始结果
/// </summary>
public sealed record PlannerResult
{
    /// <summary>分析思考</summary>
    public string? Thought { get; init; }

    /// <summary>任务列表</summary>
    public required PlannerTaskDto[] Tasks { get; init; }

    /// <summary>输出格式</summary>
    public string? OutputFormat { get; init; }
}

/// <summary>
/// 任务 DTO（LLM 输出格式）
/// </summary>
public sealed record PlannerTaskDto
{
    /// <summary>任务 ID（整数）</summary>
    public int Id { get; init; }

    /// <summary>任务名称</summary>
    public required string Name { get; init; }

    /// <summary>任务描述</summary>
    public required string Desc { get; init; }

    /// <summary>依赖的任务 ID 列表</summary>
    public int[] DependsOn { get; init; } = [];

    /// <summary>可用工具列表</summary>
    public string[] AvailableTools { get; init; } = [];

    /// <summary>需要的 Artifacts</summary>
    public string[] RequiredArtifacts { get; init; } = [];

    /// <summary>产出的 Artifacts</summary>
    public string[] OutputArtifacts { get; init; } = [];
}
