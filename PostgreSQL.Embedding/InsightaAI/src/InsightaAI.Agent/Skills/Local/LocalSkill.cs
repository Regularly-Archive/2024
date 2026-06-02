namespace InsightaAI.Agent.Skills.Local;

/// <summary>
/// 本地 Skill 实现
/// </summary>
internal class LocalSkill : ISkill
{
    private readonly string _skillDirectory;

    public SkillMetadata Metadata { get; }
    public string Instructions { get; }

    public LocalSkill(SkillMetadata metadata, string instructions, string skillDirectory)
    {
        Metadata = metadata;
        Instructions = instructions;
        _skillDirectory = skillDirectory;
    }

    public Task<string?> ReadResourceAsync(string relativePath)
    {
        // 安全检查：防止路径遍历攻击
        var normalizedPath = Path.GetFullPath(Path.Combine(_skillDirectory, relativePath));
        if (!normalizedPath.StartsWith(_skillDirectory))
        {
            return Task.FromResult<string?>(null);
        }

        if (!File.Exists(normalizedPath))
        {
            return Task.FromResult<string?>(null);
        }

        return File.ReadAllTextAsync(normalizedPath);
    }
}
