using InsightaAI.Agents.Subagents.Definitions;

namespace InsightaAI.Agents.Subagents.Invocation;

/// <summary>Host-specific executor for one kind of subagent definition.</summary>
public interface ISubagentAdapter
{
    bool CanInvoke(SubagentDefinition definition);

    Task<SubagentInvocationResult> InvokeAsync(
        SubagentInvocationRequest request,
        CancellationToken cancellationToken = default);
}
