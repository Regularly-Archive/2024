using System.Collections.Concurrent;

namespace InsightaAI.Agent.Skills;

/// <summary>
/// Skill 注册表实现
/// </summary>
public class SkillRegistry : ISkillRegistry
{
    private readonly List<ISkillProvider> _providers = [];
    private readonly ConcurrentDictionary<string, ISkill> _activeSkills = new();
    private readonly ConcurrentDictionary<string, SkillMetadata> _metadataCache = new();

    public void RegisterProvider(ISkillProvider provider)
    {
        _providers.Add(provider);
    }

    public async Task<IReadOnlyList<SkillMetadata>> ListAllSkillsAsync(CancellationToken cancellationToken = default)
    {
        var allMetadata = new List<SkillMetadata>();

        foreach (var provider in _providers)
        {
            await foreach (var metadata in provider.ListSkillsAsync(cancellationToken))
            {
                allMetadata.Add(metadata);
                _metadataCache.TryAdd(metadata.Name, metadata);
            }
        }

        return allMetadata;
    }

    public async Task<ISkill?> ActivateAsync(string skillName, CancellationToken cancellationToken = default)
    {
        // 已激活则直接返回
        if (_activeSkills.TryGetValue(skillName, out var existing))
        {
            return existing;
        }

        // 从所有提供者中查找并加载
        foreach (var provider in _providers)
        {
            var skill = await provider.LoadSkillAsync(skillName, cancellationToken);
            if (skill != null)
            {
                _activeSkills.TryAdd(skillName, skill);
                return skill;
            }
        }

        return null;
    }

    public IReadOnlyList<ISkill> GetActiveSkills()
    {
        return _activeSkills.Values.ToList();
    }

    public void Deactivate(string skillName)
    {
        _activeSkills.TryRemove(skillName, out _);
    }

    public bool IsActive(string skillName)
    {
        return _activeSkills.ContainsKey(skillName);
    }
}
