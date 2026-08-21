namespace InsightaAI.Agents.Subagents.Invocation;

/// <summary>Routes a resolved definition to exactly one registered host adapter.</summary>
public sealed class SubagentDispatcher
{
    private readonly IReadOnlyList<ISubagentAdapter> _adapters;

    public SubagentDispatcher(IEnumerable<ISubagentAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _adapters = adapters.ToArray();
    }

    public Task<SubagentInvocationResult> InvokeAsync(
        SubagentInvocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var matches = _adapters.Where(adapter => adapter.CanInvoke(request.Definition)).Take(2).ToArray();
        return matches.Length switch
        {
            1 => matches[0].InvokeAsync(request, cancellationToken),
            0 => throw new InvalidOperationException(
                $"No subagent adapter is registered for '{request.Definition.Id}' ({request.Definition.AdapterKey})."),
            _ => throw new InvalidOperationException(
                $"Multiple subagent adapters can invoke '{request.Definition.Id}' ({request.Definition.AdapterKey}).")
        };
    }
}
