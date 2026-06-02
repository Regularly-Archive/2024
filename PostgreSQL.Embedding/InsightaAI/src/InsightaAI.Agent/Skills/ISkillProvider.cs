namespace InsightaAI.Agent.Skills;

/// <summary>
/// Skill 提供者（不同来源实现不同）
/// </summary>
public interface ISkillProvider
{
    /// <summary>
    /// 提供者名称
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// 扫描发现所有 skills（只返回元数据，不加载完整内容）
    /// </summary>
    IAsyncEnumerable<SkillMetadata> ListSkillsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 加载完整 skill（包括 instructions 和资源）
    /// </summary>
    Task<ISkill?> LoadSkillAsync(string skillName, CancellationToken cancellationToken = default);
}
