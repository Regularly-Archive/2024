using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Tools;
using InsightaAI.Agents.Subagents.Catalog;
using InsightaAI.Agents.Subagents.Invocation;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>CLI host bridge from DelegateTool to named local Subagent invocations.</summary>
public sealed class CliSubagentDelegationHandler : IAgentDelegationHandler
{
    private const int MaxOutputCharacters = 12_000;
    private readonly ISubagentCatalog _catalog;
    private readonly SubagentDispatcher _dispatcher;
    private readonly string _userId;

    public CliSubagentDelegationHandler(ISubagentCatalog catalog, SubagentDispatcher dispatcher, string userId)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        _catalog = catalog;
        _dispatcher = dispatcher;
        _userId = userId;
    }

    public async Task<ToolResult> DelegateAsync(AgentDelegationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var definition = await _catalog.FindAsync(request.AgentId, cancellationToken);
        if (definition == null)
            return ToolResult.FromError($"Subagent '{request.AgentId}' was not found in the local catalog.");

        var invocationId = Guid.NewGuid().ToString("N");
        var result = await _dispatcher.InvokeAsync(new SubagentInvocationRequest
        {
            Definition = definition,
            Input = request.Task,
            Context = new SubagentInvocationContext
            {
                InvocationId = invocationId,
                UserId = _userId,
                ParentSessionId = request.ParentContext.SessionId,
                ParentInvocationId = request.ParentContext.ToolCallId
            },
            Progress = new ParentToolProgressReporter(request.ParentContext.Progress)
        }, cancellationToken);

        if (result.Status != SubagentInvocationStatus.Completed)
            return ToolResult.FromError(result.Error ?? $"Subagent '{definition.Id}' ended with status '{result.Status}'.");

        var output = result.Output ?? string.Empty;
        if (output.Length > MaxOutputCharacters)
            output = output[..MaxOutputCharacters] + "\n\n[Subagent output truncated by the host.]";
        return ToolResult.FromText(output);
    }

    private sealed class ParentToolProgressReporter(IToolProgressReporter parentProgress) : ISubagentProgressReporter
    {
        public ValueTask ReportAsync(SubagentProgressUpdate update, CancellationToken cancellationToken = default)
        {
            var progress = update.Kind == SubagentProgressKind.Output
                ? new ToolProgressUpdate
                {
                    Kind = ToolProgressKind.Output,
                    Text = update.Text,
                    Stream = update.Stream switch
                    {
                        SubagentOutputStream.Stderr => ToolOutputStream.Stderr,
                        _ => ToolOutputStream.Stdout
                    }
                }
                : new ToolProgressUpdate
                {
                    Kind = ToolProgressKind.Status,
                    Message = update.Message
                };
            return parentProgress.ReportAsync(progress, cancellationToken);
        }
    }
}
