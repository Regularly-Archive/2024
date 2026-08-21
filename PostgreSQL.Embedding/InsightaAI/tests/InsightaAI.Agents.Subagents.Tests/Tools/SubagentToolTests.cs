using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Cli.Services;
using InsightaAI.Agent.Tools;
using InsightaAI.Agents.Subagents.Catalog;
using InsightaAI.Agents.Subagents.Definitions;
using InsightaAI.Agents.Subagents.Invocation;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agents.Subagents.Tests.Tools;

public sealed class DelegateToolTests
{
    [Fact]
    public async Task ExecuteAsync_ResolvedDefinition_UsesHostIdentityAndParentLinkage()
    {
        var definition = new InsightaSubagentDefinition { Id = "reviewer", Name = "Reviewer" };
        var catalog = new FixedCatalog(definition);
        var adapter = new RecordingAdapter();
        var registry = new ToolRegistry();
        registry.Register(new DelegateTool(new CliSubagentDelegationHandler(
            catalog, new SubagentDispatcher([adapter]), "host-user")));

        var result = await ExecuteAsync(registry, """{ "agent_id": "reviewer", "task": "Review this change" }""");

        Assert.False(result.IsError);
        Assert.Equal("reviewed", Assert.IsType<TextBlock>(result.Content.Single()).Text);
        Assert.NotNull(adapter.Request);
        Assert.Equal("host-user", adapter.Request!.Context.UserId);
        Assert.Equal("parent-session", adapter.Request.Context.ParentSessionId);
        Assert.Equal("parent-call", adapter.Request.Context.ParentInvocationId);
        Assert.Equal("Review this change", adapter.Request.Input);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownDefinition_ReturnsError()
    {
        var registry = new ToolRegistry();
        registry.Register(new DelegateTool(new CliSubagentDelegationHandler(
            new FixedCatalog(null), new SubagentDispatcher([new RecordingAdapter()]), "host-user")));

        var result = await ExecuteAsync(registry, """{ "agent_id": "missing", "task": "Review this change" }""");

        Assert.True(result.IsError);
        Assert.Contains("was not found", Assert.IsType<TextBlock>(result.Content.Single()).Text);
    }

    private static Task<ToolResult> ExecuteAsync(ToolRegistry registry, string arguments)
    {
        using var document = JsonDocument.Parse(arguments);
        return registry.ExecuteAsync(new ToolCallBlock
        {
            Id = "parent-call",
            Name = "delegate",
            Arguments = document.RootElement.Clone()
        }, new ToolExecutionContext
        {
            AgentId = "cli-agent",
            ToolCallId = "parent-call",
            SessionId = "parent-session"
        });
    }

    private sealed class FixedCatalog(SubagentDefinition? definition) : ISubagentCatalog
    {
        public ValueTask<SubagentDefinition?> FindAsync(string id, CancellationToken cancellationToken = default) => ValueTask.FromResult(definition);

        public async IAsyncEnumerable<SubagentDefinition> ListAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (definition != null)
                yield return definition;
            await Task.CompletedTask;
        }
    }

    private sealed class RecordingAdapter : ISubagentAdapter
    {
        public SubagentInvocationRequest? Request { get; private set; }
        public bool CanInvoke(SubagentDefinition definition) => definition is InsightaSubagentDefinition;

        public Task<SubagentInvocationResult> InvokeAsync(SubagentInvocationRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new SubagentInvocationResult
            {
                InvocationId = "child-call",
                Status = SubagentInvocationStatus.Completed,
                Output = "reviewed"
            });
        }
    }
}
