using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Security;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;
using System.Text.Json;

namespace InsightaAI.Agent.Tests;

public sealed class SecurityPolicyHookTests
{
    [Theory]
    [InlineData(DenyMatchMode.Exact, "Remove-Item -Recurse C:\\temp", "  remove-item   -recurse   c:\\temp ")]
    [InlineData(DenyMatchMode.Glob, "rm -rf *", "rm -rf /var/tmp")]
    [InlineData(DenyMatchMode.Regex, "(?i)remove-item\\s+-recurse", "Remove-Item -Recurse C:\\temp")]
    public async Task Denies_Bash_Command_When_Rule_Matches(DenyMatchMode mode, string pattern, string command)
    {
        var hook = new SecurityPolicyHook([new DenyRule(pattern, mode)]);

        var arguments = System.Text.Json.JsonSerializer.Serialize(new { command });
        var result = await hook.OnBeforeExecutionAsync("bash", arguments, CreateContext());

        Assert.Equal(ToolHookResult.DenyByPolicy, result);
    }

    [Fact]
    public async Task Allows_When_No_Rule_Matches()
    {
        var hook = new SecurityPolicyHook([new DenyRule("rm -rf *", DenyMatchMode.Glob)]);

        var result = await hook.OnBeforeExecutionAsync(
            "bash", "{\"command\":\"dotnet test\"}", CreateContext());

        Assert.Equal(ToolHookResult.Allow, result);
    }

    [Fact]
    public async Task Evaluates_When_Tool_Is_Always_Allowed()
    {
        var hook = new SecurityPolicyHook([]);

        var result = await hook.OnBeforeExecutionAsync("bash", "{\"command\":\"dotnet test\"}", CreateContext());

        Assert.True(hook.EvaluateWhenToolAlwaysAllowed);
        Assert.Equal(ToolHookResult.Allow, result);
    }

    [Fact]
    public async Task Deny_Rule_Cannot_Be_Bypassed_By_AllowAlways()
    {
        var executed = false;
        var registry = new ToolRegistry().RegisterFunction(
            "bash", "A protected test tool.", JsonDocument.Parse("{}").RootElement.Clone(),
            (_, _) =>
            {
                executed = true;
                return Task.FromResult(ToolResult.FromText("executed"));
            });
        var toolCall = new ToolCallBlock
        {
            Id = "call-bash",
            Name = "bash",
            Arguments = JsonDocument.Parse("{\"command\":\"Remove-Item -Recurse C:\\\\temp\"}").RootElement.Clone()
        };
        using var llmClient = new MockLlmClient(firstResponseToolCalls: [toolCall], secondResponse: "done");
        using var agent = new Agent(
            new AgentConfig { Id = "security-test", Name = "Security Test", Model = "test-model" },
            llmClient,
            registry);

        agent.AddHook(new AllowAlwaysHook());
        agent.AddHook(new SecurityPolicyHook([new DenyRule("*", DenyMatchMode.Glob)]));

        var events = new List<AgentEvent>();
        await foreach (var @event in agent.RunStreamAsync("Run the protected tool."))
        {
            events.Add(@event);
        }

        Assert.False(executed);
        var toolEnd = Assert.Single(events.OfType<AgentToolEndEvent>());
        Assert.Contains("security policy", toolEnd.ResultPreview);
    }

    private static ToolExecutionContext CreateContext() => new()
    {
        AgentId = "security-test",
        ToolCallId = "call-security"
    };

    private sealed class AllowAlwaysHook : IToolHook
    {
        public Task<ToolHookResult> OnBeforeExecutionAsync(
            string toolName,
            string arguments,
            ToolExecutionContext context) => Task.FromResult(ToolHookResult.AllowAlways);
    }
}
