using InsightaAI.Agent.Context.SystemPrompt;
using InsightaAI.Agent.Skills;

namespace InsightaAI.Agent.Tests.Context;

public class SystemPromptBuilderTests
{
    [Fact]
    public async Task BuildAsync_Should_Include_Custom_Instructions()
    {
        var result = await SystemPromptBuilder.BuildAsync(new SystemPromptParams
        {
            CustomInstructions = "Always answer in Chinese."
        });

        Assert.Contains("Always answer in Chinese.", result);
    }

    [Fact]
    public async Task BuildAsync_Should_List_Activated_Skill_In_Available_Skills()
    {
        var metadata = new SkillMetadata
        {
            Name = "test-skill",
            Description = "Test skill description"
        };

        var result = await SystemPromptBuilder.BuildAsync(new SystemPromptParams
        {
            CustomInstructions = "",
            AllSkills = [metadata],
            ActivatedSkills = [new TestSkill(metadata, "Test skill instructions")]
        });

        Assert.Contains("- **test-skill**: Test skill description", result);
        Assert.Contains("Test skill instructions", result);
    }

    [Fact]
    public async Task BuildAsync_Should_Order_All_Skills_By_Name()
    {
        var result = await SystemPromptBuilder.BuildAsync(new SystemPromptParams
        {
            CustomInstructions = "",
            AllSkills =
            [
                new SkillMetadata { Name = "zeta", Description = "Zeta description" },
                new SkillMetadata { Name = "alpha", Description = "Alpha description" }
            ]
        });

        var alphaIndex = result.IndexOf("- **alpha**: Alpha description", StringComparison.Ordinal);
        var zetaIndex = result.IndexOf("- **zeta**: Zeta description", StringComparison.Ordinal);

        Assert.True(alphaIndex >= 0);
        Assert.True(zetaIndex > alphaIndex);
    }

    private sealed class TestSkill(SkillMetadata metadata, string instructions) : ISkill
    {
        public SkillMetadata Metadata { get; } = metadata;
        public string Instructions { get; } = instructions;

        public Task<string?> ReadResourceAsync(string relativePath) => Task.FromResult<string?>(null);
    }
}
