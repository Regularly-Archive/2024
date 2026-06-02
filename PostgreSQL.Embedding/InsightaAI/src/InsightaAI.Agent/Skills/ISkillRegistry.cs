namespace InsightaAI.Agent.Skills;

/// <summary>
/// Skill 注册表，管理所有可用的 Skills
/// </summary>
public interface ISkillRegistry
{
    /// <summary>
    /// 注册 Skill 提供者
    /// </summary>
    void RegisterProvider(ISkillProvider provider);

    /// <summary>
    /// 列出所有可用 Skill 的元数据
    /// </summary>
    Task<IReadOnlyList<SkillMetadata>> ListAllSkillsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 激活 Skill（加载完整内容）
    /// </summary>
    Task<ISkill?> ActivateAsync(string skillName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取已激活的 Skills
    /// </summary>
    IReadOnlyList<ISkill> GetActiveSkills();

    /// <summary>
    /// 停用 Skill
    /// </summary>
    void Deactivate(string skillName);

    /// <summary>
    /// 检查 Skill 是否已激活
    /// </summary>
    bool IsActive(string skillName);
}
