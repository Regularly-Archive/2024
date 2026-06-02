namespace InsightaAI.Agent.Skills;

/// <summary>
/// Skill 元数据（轻量级，用于启动时加载）
/// </summary>
public record SkillMetadata
{
    /// <summary>
    /// Skill 名称（小写字母、数字、连字符）
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Skill 描述（做什么、什么时候用）
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// 预批准的工具列表（空格分隔），如 "Read Grep Bash(git:*)"
    /// </summary>
    public string? AllowedTools { get; init; }

    /// <summary>
    /// 来源提供者名称
    /// </summary>
    public string? ProviderName { get; init; }
}
