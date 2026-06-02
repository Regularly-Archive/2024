using InsightaAI.Agent.Skills;
using InsightaAI.Agent.Skills.Local;

namespace InsightaAI.Agent.Tests.Skills;

/// <summary>
/// LocalSkillProvider 测试
/// </summary>
public class LocalSkillProviderTests : IDisposable
{
    private readonly string _tempDir;

    public LocalSkillProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"insighta-skills-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task ListSkillsAsync_Should_Return_Empty_When_No_Skills()
    {
        // Arrange
        var provider = new LocalSkillProvider(_tempDir);

        // Act
        var skills = new List<SkillMetadata>();
        await foreach (var skill in provider.ListSkillsAsync())
        {
            skills.Add(skill);
        }

        // Assert
        Assert.Empty(skills);
    }

    [Fact]
    public async Task ListSkillsAsync_Should_Return_Empty_When_Directory_Not_Exists()
    {
        // Arrange
        var nonExistentDir = Path.Combine(_tempDir, "non-existent");
        var provider = new LocalSkillProvider(nonExistentDir);

        // Act
        var skills = new List<SkillMetadata>();
        await foreach (var skill in provider.ListSkillsAsync())
        {
            skills.Add(skill);
        }

        // Assert
        Assert.Empty(skills);
    }

    [Fact]
    public async Task ListSkillsAsync_Should_Parse_Metadata()
    {
        // Arrange
        CreateSkill("code-review", """
            ---
            name: code-review
            description: 代码审查助手，当用户要求审查代码时使用。
            ---
            你是一个代码审查专家。
            """);

        var provider = new LocalSkillProvider(_tempDir);

        // Act
        var skills = new List<SkillMetadata>();
        await foreach (var skill in provider.ListSkillsAsync())
        {
            skills.Add(skill);
        }

        // Assert
        Assert.Single(skills);
        Assert.Equal("code-review", skills[0].Name);
        Assert.Contains("代码审查", skills[0].Description);
    }

    [Fact]
    public async Task ListSkillsAsync_Should_Parse_AllowedTools()
    {
        // Arrange
        CreateSkill("test-skill", """
            ---
            name: test-skill
            description: 测试 Skill
            allowed-tools: Read Grep Bash(git:*)
            ---
            Instructions here.
            """);

        var provider = new LocalSkillProvider(_tempDir);

        // Act
        var skills = new List<SkillMetadata>();
        await foreach (var skill in provider.ListSkillsAsync())
        {
            skills.Add(skill);
        }

        // Assert
        Assert.Single(skills);
        Assert.Equal("Read Grep Bash(git:*)", skills[0].AllowedTools);
    }

    [Fact]
    public async Task ListSkillsAsync_Should_Skip_Invalid_Skills()
    {
        // Arrange
        CreateSkill("valid-skill", """
            ---
            name: valid-skill
            description: 有效的 Skill
            ---
            Instructions.
            """);

        // 创建一个没有 SKILL.md 的目录
        Directory.CreateDirectory(Path.Combine(_tempDir, "no-skill-md"));

        // 创建一个 SKILL.md 缺少必填字段的 skill
        CreateSkill("invalid-skill", """
            ---
            name: invalid-skill
            ---
            Missing description.
            """);

        var provider = new LocalSkillProvider(_tempDir);

        // Act
        var skills = new List<SkillMetadata>();
        await foreach (var skill in provider.ListSkillsAsync())
        {
            skills.Add(skill);
        }

        // Assert
        Assert.Single(skills);
        Assert.Equal("valid-skill", skills[0].Name);
    }

    [Fact]
    public async Task ListSkillsAsync_Should_Parse_Multiple_Skills()
    {
        // Arrange
        CreateSkill("skill-a", """
            ---
            name: skill-a
            description: Skill A
            ---
            Instructions A.
            """);

        CreateSkill("skill-b", """
            ---
            name: skill-b
            description: Skill B
            ---
            Instructions B.
            """);

        var provider = new LocalSkillProvider(_tempDir);

        // Act
        var skills = new List<SkillMetadata>();
        await foreach (var skill in provider.ListSkillsAsync())
        {
            skills.Add(skill);
        }

        // Assert
        Assert.Equal(2, skills.Count);
        Assert.Contains(skills, s => s.Name == "skill-a");
        Assert.Contains(skills, s => s.Name == "skill-b");
    }

    [Fact]
    public async Task LoadSkillAsync_Should_Return_Null_When_Not_Found()
    {
        // Arrange
        var provider = new LocalSkillProvider(_tempDir);

        // Act
        var skill = await provider.LoadSkillAsync("non-existent");

        // Assert
        Assert.Null(skill);
    }

    [Fact]
    public async Task LoadSkillAsync_Should_Load_Full_Content()
    {
        // Arrange
        CreateSkill("test-skill", """
            ---
            name: test-skill
            description: 测试 Skill
            allowed-tools: Read Write
            ---
            # 使用说明

            这是一个测试 Skill。

            ## 步骤

            1. 读取文件
            2. 写入文件
            """);

        var provider = new LocalSkillProvider(_tempDir);

        // Act
        var skill = await provider.LoadSkillAsync("test-skill");

        // Assert
        Assert.NotNull(skill);
        Assert.Equal("test-skill", skill.Metadata.Name);
        Assert.Contains("# 使用说明", skill.Instructions);
        Assert.Contains("## 步骤", skill.Instructions);
    }

    [Fact]
    public async Task LoadSkillAsync_Should_Read_Resources()
    {
        // Arrange
        var skillDir = Path.Combine(_tempDir, "test-skill");
        Directory.CreateDirectory(skillDir);

        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), """
            ---
            name: test-skill
            description: 测试 Skill
            ---
            Instructions.
            """);

        var referencesDir = Path.Combine(skillDir, "references");
        Directory.CreateDirectory(referencesDir);
        File.WriteAllText(Path.Combine(referencesDir, "guide.md"), "# 参考指南");

        var provider = new LocalSkillProvider(_tempDir);

        // Act
        var skill = await provider.LoadSkillAsync("test-skill");
        var resource = await skill!.ReadResourceAsync("references/guide.md");

        // Assert
        Assert.NotNull(resource);
        Assert.Equal("# 参考指南", resource);
    }

    [Fact]
    public async Task LoadSkillAsync_Should_Return_Null_For_Missing_Resource()
    {
        // Arrange
        CreateSkill("test-skill", """
            ---
            name: test-skill
            description: 测试 Skill
            ---
            Instructions.
            """);

        var provider = new LocalSkillProvider(_tempDir);

        // Act
        var skill = await provider.LoadSkillAsync("test-skill");
        var resource = await skill!.ReadResourceAsync("non-existent.md");

        // Assert
        Assert.Null(resource);
    }

    [Fact]
    public async Task LoadSkillAsync_Should_Reject_Path_Traversal()
    {
        // Arrange
        CreateSkill("test-skill", """
            ---
            name: test-skill
            description: 测试 Skill
            ---
            Instructions.
            """);

        var provider = new LocalSkillProvider(_tempDir);

        // Act
        var skill = await provider.LoadSkillAsync("test-skill");
        var resource = await skill!.ReadResourceAsync("../../../etc/passwd");

        // Assert
        Assert.Null(resource);
    }

    [Fact]
    public async Task LoadSkillAsync_Should_Reject_Name_Mismatch()
    {
        // Arrange - 目录名和 SKILL.md 中的 name 不一致
        CreateSkill("dir-name", """
            ---
            name: different-name
            description: 测试 Skill
            ---
            Instructions.
            """);

        var provider = new LocalSkillProvider(_tempDir);

        // Act
        var skill = await provider.LoadSkillAsync("dir-name");

        // Assert
        Assert.Null(skill);
    }

    private void CreateSkill(string name, string skillMdContent)
    {
        var skillDir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), skillMdContent);
    }
}
