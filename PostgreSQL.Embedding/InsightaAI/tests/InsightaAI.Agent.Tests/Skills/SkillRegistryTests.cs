using InsightaAI.Agent.Skills;

namespace InsightaAI.Agent.Tests.Skills;

/// <summary>
/// SkillRegistry 测试
/// </summary>
public class SkillRegistryTests
{
    [Fact]
    public async Task ListAllSkillsAsync_Should_Return_Empty_When_No_Providers()
    {
        // Arrange
        var registry = new SkillRegistry();

        // Act
        var skills = await registry.ListAllSkillsAsync();

        // Assert
        Assert.Empty(skills);
    }

    [Fact]
    public async Task ListAllSkillsAsync_Should_Aggregate_From_Multiple_Providers()
    {
        // Arrange
        var registry = new SkillRegistry();
        registry.RegisterProvider(new MockSkillProvider("provider-a", [
            new() { Name = "skill-a", Description = "Skill A" }
        ]));
        registry.RegisterProvider(new MockSkillProvider("provider-b", [
            new() { Name = "skill-b", Description = "Skill B" }
        ]));

        // Act
        var skills = await registry.ListAllSkillsAsync();

        // Assert
        Assert.Equal(2, skills.Count);
        Assert.Contains(skills, s => s.Name == "skill-a");
        Assert.Contains(skills, s => s.Name == "skill-b");
    }

    [Fact]
    public async Task ActivateAsync_Should_Return_Null_When_Not_Found()
    {
        // Arrange
        var registry = new SkillRegistry();
        registry.RegisterProvider(new MockSkillProvider("provider", []));

        // Act
        var skill = await registry.ActivateAsync("non-existent");

        // Assert
        Assert.Null(skill);
    }

    [Fact]
    public async Task ActivateAsync_Should_Load_Skill()
    {
        // Arrange
        var registry = new SkillRegistry();
        var mockSkill = new MockSkill("test-skill", "Test instructions");
        registry.RegisterProvider(new MockSkillProvider("provider", [], [mockSkill]));

        // Act
        var skill = await registry.ActivateAsync("test-skill");

        // Assert
        Assert.NotNull(skill);
        Assert.Equal("test-skill", skill.Metadata.Name);
        Assert.Equal("Test instructions", skill.Instructions);
    }

    [Fact]
    public async Task ActivateAsync_Should_Cache_Activated_Skill()
    {
        // Arrange
        var registry = new SkillRegistry();
        var mockSkill = new MockSkill("test-skill", "Test instructions");
        registry.RegisterProvider(new MockSkillProvider("provider", [], [mockSkill]));

        // Act
        var skill1 = await registry.ActivateAsync("test-skill");
        var skill2 = await registry.ActivateAsync("test-skill");

        // Assert
        Assert.Same(skill1, skill2);
    }

    [Fact]
    public async Task GetActiveSkills_Should_Return_Activated_Skills()
    {
        // Arrange
        var registry = new SkillRegistry();
        var mockSkillA = new MockSkill("skill-a", "Instructions A");
        var mockSkillB = new MockSkill("skill-b", "Instructions B");
        registry.RegisterProvider(new MockSkillProvider("provider", [], [mockSkillA, mockSkillB]));

        // Act
        await registry.ActivateAsync("skill-a");
        var activeSkills = registry.GetActiveSkills();

        // Assert
        Assert.Single(activeSkills);
        Assert.Equal("skill-a", activeSkills[0].Metadata.Name);
    }

    [Fact]
    public async Task Deactivate_Should_Remove_Activated_Skill()
    {
        // Arrange
        var registry = new SkillRegistry();
        var mockSkill = new MockSkill("test-skill", "Test instructions");
        registry.RegisterProvider(new MockSkillProvider("provider", [], [mockSkill]));

        await registry.ActivateAsync("test-skill");
        Assert.True(registry.IsActive("test-skill"));

        // Act
        registry.Deactivate("test-skill");

        // Assert
        Assert.False(registry.IsActive("test-skill"));
        Assert.Empty(registry.GetActiveSkills());
    }

    [Fact]
    public async Task IsActive_Should_Return_True_For_Activated_Skill()
    {
        // Arrange
        var registry = new SkillRegistry();
        var mockSkill = new MockSkill("test-skill", "Test instructions");
        registry.RegisterProvider(new MockSkillProvider("provider", [], [mockSkill]));

        // Act & Assert
        Assert.False(registry.IsActive("test-skill"));
        await registry.ActivateAsync("test-skill");
        Assert.True(registry.IsActive("test-skill"));
    }

    // ============================================================
    // Mock 类
    // ============================================================

    private class MockSkillProvider : ISkillProvider
    {
        private readonly List<SkillMetadata> _metadata;
        private readonly List<ISkill> _skills;

        public string ProviderName { get; }

        public MockSkillProvider(string name, List<SkillMetadata> metadata, List<ISkill>? skills = null)
        {
            ProviderName = name;
            _metadata = metadata;
            _skills = skills ?? [];
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators
        public async IAsyncEnumerable<SkillMetadata> ListSkillsAsync(CancellationToken cancellationToken = default)
#pragma warning restore CS1998
        {
            foreach (var m in _metadata)
            {
                yield return m;
            }
        }

        public Task<ISkill?> LoadSkillAsync(string skillName, CancellationToken cancellationToken = default)
        {
            var skill = _skills.FirstOrDefault(s => s.Metadata.Name == skillName);
            return Task.FromResult(skill);
        }
    }

    private class MockSkill : ISkill
    {
        public SkillMetadata Metadata { get; }
        public string Instructions { get; }

        public MockSkill(string name, string instructions)
        {
            Metadata = new SkillMetadata { Name = name, Description = $"Mock skill: {name}" };
            Instructions = instructions;
        }

        public Task<string?> ReadResourceAsync(string relativePath)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
