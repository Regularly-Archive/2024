namespace InsightaAI.Agent.Skills;

/// <summary>
/// Skill 的抽象（激活时加载）
/// </summary>
public interface ISkill
{
    /// <summary>
    /// Skill 元数据
    /// </summary>
    SkillMetadata Metadata { get; }

    /// <summary>
    /// Skill 指令内容（SKILL.md body）
    /// </summary>
    string Instructions { get; }

    /// <summary>
    /// 按需读取资源文件
    /// </summary>
    /// <param name="relativePath">相对于 skill 根目录的路径，如 "references/checklist.md"</param>
    /// <returns>文件内容，不存在返回 null</returns>
    Task<string?> ReadResourceAsync(string relativePath);
}
