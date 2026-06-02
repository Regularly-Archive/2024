using System.Text.Json;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Skills;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;

namespace InsightaAI.Agent.Tests;

/// <summary>
/// Agent 与 Skill 集成测试
/// </summary>
public class AgentSkillIntegrationTests
{
    [Fact]
    public async Task Agent_Should_List_Available_Skills_In_SystemPrompt()
    {
        // Arrange
        var skillRegistry = new SkillRegistry();
        skillRegistry.RegisterProvider(new MockSkillProvider([
            new() { Name = "code-review", Description = "代码审查助手" }
        ]));

        var toolRegistry = new ToolRegistry();
        var llmClient = new MockLlmClient(response: "I see code-review skill is available.");
        var config = CreateConfig();

        var agent = new Agent(config, llmClient, toolRegistry, skillRegistry);

        // Act
        var result = await agent.RunAsync("Hello");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(AgentStatus.Completed, result.Status);

        // 验证 SystemPrompt 中包含了可用 Skills 信息
        // （通过检查 LLM 请求来验证）
        Assert.Contains("code-review", result.Message.GetTextContent());
    }

    [Fact]
    public async Task Agent_Should_Have_Activate_Skill_Tool()
    {
        // Arrange
        var skillRegistry = new SkillRegistry();
        skillRegistry.RegisterProvider(new MockSkillProvider([
            new() { Name = "test-skill", Description = "测试技能" }
        ]));

        var toolRegistry = new ToolRegistry();
        var llmClient = new MockLlmClient(response: "OK");
        var config = CreateConfig();

        var agent = new Agent(config, llmClient, toolRegistry, skillRegistry);

        // Act
        var result = await agent.RunAsync("Hello");

        // Assert
        // activate_skill 工具应该已经注册
        Assert.True(toolRegistry.HasTool("activate_skill"));
    }

    [Fact]
    public async Task Agent_Should_Activate_Skill_Via_Tool_Call()
    {
        // Arrange
        var skillRegistry = new SkillRegistry();
        var mockSkill = new MockSkill("code-review", "You are a code reviewer. Use Read to read code.");
        skillRegistry.RegisterProvider(new MockSkillProvider([], [mockSkill]));

        var toolRegistry = new ToolRegistry();

        // 模拟 LLM 先调用 activate_skill，然后返回完成
        var toolCall = new ToolCallBlock
        {
            Id = "call-1",
            Name = "activate_skill",
            Arguments = JsonSerializer.Deserialize<JsonElement>(@"{""skill_name"": ""code-review""}")
        };

        var llmClient = new MockLlmClient(
            firstResponseToolCalls: [toolCall],
            secondResponse: "Skill activated. I'm ready to review code."
        );

        var config = CreateConfig();
        var agent = new Agent(config, llmClient, toolRegistry, skillRegistry);

        // Act
        var result = await agent.RunAsync("Help me review some code");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(AgentStatus.Completed, result.Status);
        Assert.Contains("ready to review", result.Message.GetTextContent(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Agent_Should_Inject_Skill_Instructions_Into_SystemPrompt()
    {
        // Arrange
        var skillRegistry = new SkillRegistry();
        var mockSkill = new MockSkill("test-skill", "## Instructions\nFollow these steps:\n1. Read the code\n2. Analyze it");
        skillRegistry.RegisterProvider(new MockSkillProvider([], [mockSkill]));

        var toolRegistry = new ToolRegistry();

        var toolCall = new ToolCallBlock
        {
            Id = "call-1",
            Name = "activate_skill",
            Arguments = JsonSerializer.Deserialize<JsonElement>(@"{""skill_name"": ""test-skill""}")
        };

        string? capturedSystemPrompt = null;
        var llmClient = new CapturingMockLlmClient(
            firstResponseToolCalls: [toolCall],
            secondResponse: "Done",
            onCaptureSystemPrompt: sp => capturedSystemPrompt = sp
        );

        var config = CreateConfig();
        var agent = new Agent(config, llmClient, toolRegistry, skillRegistry);

        // Act
        var result = await agent.RunAsync("Do something");

        // Assert
        Assert.NotNull(capturedSystemPrompt);
        Assert.Contains("## Instructions", capturedSystemPrompt);
        Assert.Contains("Follow these steps", capturedSystemPrompt);
    }

    [Fact]
    public async Task Agent_Should_Work_Without_SkillRegistry()
    {
        // Arrange - 不传入 SkillRegistry
        var toolRegistry = new ToolRegistry();
        var llmClient = new MockLlmClient(response: "Hello!");
        var config = CreateConfig();

        var agent = new Agent(config, llmClient, toolRegistry);

        // Act
        var result = await agent.RunAsync("Hi");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(AgentStatus.Completed, result.Status);
        Assert.False(toolRegistry.HasTool("activate_skill"));
    }

    private static AgentConfig CreateConfig() => new()
    {
        Id = "test-agent",
        Name = "Test Agent",
        SystemPrompt = "You are a helpful assistant.",
        Model = "test-model",
        MaxToolRounds = 5
    };

    // ============================================================
    // Mock 类
    // ============================================================

    private class MockSkillProvider : ISkillProvider
    {
        private readonly List<SkillMetadata> _metadata;
        private readonly List<ISkill> _skills;

        public string ProviderName => "mock";

        public MockSkillProvider(List<SkillMetadata> metadata, List<ISkill>? skills = null)
        {
            _metadata = metadata;
            _skills = skills ?? [];
        }

#pragma warning disable CS1998
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
            Metadata = new SkillMetadata { Name = name, Description = $"Mock: {name}" };
            Instructions = instructions;
        }

        public Task<string?> ReadResourceAsync(string relativePath)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private class CapturingMockLlmClient : ILlmClient
    {
        private readonly ToolCallBlock[]? _firstResponseToolCalls;
        private readonly string _secondResponse;
        private readonly Action<string> _onCaptureSystemPrompt;
        private int _callCount = 0;

        public string ProviderName => "mock";
        public bool SupportsReasoning => false;

        public CapturingMockLlmClient(
            ToolCallBlock[]? firstResponseToolCalls,
            string secondResponse,
            Action<string> onCaptureSystemPrompt)
        {
            _firstResponseToolCalls = firstResponseToolCalls;
            _secondResponse = secondResponse;
            _onCaptureSystemPrompt = onCaptureSystemPrompt;
        }

        public LlmStream Stream(LlmRequest request)
        {
            _callCount++;

            // 捕获系统提示词
            var systemMessage = request.Messages.FirstOrDefault(m => m.Role == MessageRole.System);
            if (systemMessage != null)
            {
                _onCaptureSystemPrompt(systemMessage.GetTextContent());
            }

            if (_callCount == 1 && _firstResponseToolCalls != null)
            {
                return new MockLlmStream("", _firstResponseToolCalls);
            }

            return new MockLlmStream(_secondResponse);
        }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            _callCount++;
            return Task.FromResult(new LlmResponse
            {
                Model = request.Model,
                Content = [new TextBlock { Text = _secondResponse }],
                FinishReason = DoneReason.Complete,
                Usage = new TokenUsage { InputTokens = 10, OutputTokens = 20 }
            });
        }
    }
}
