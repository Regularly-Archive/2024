using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools;

/// <summary>Host-provided execution boundary for a named Agent delegation.</summary>
public interface IAgentDelegationHandler
{
    Task<ToolResult> DelegateAsync(
        AgentDelegationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Validated arguments and parent context for one delegation request.</summary>
public sealed record AgentDelegationRequest
{
    public required string AgentId { get; init; }
    public required string Task { get; init; }
    public required ToolExecutionContext ParentContext { get; init; }
}

/// <summary>
/// 委托工具 - 将任务委托给其他 Agent
/// </summary>
public class DelegateTool : ITool, IToolResultProjector
{
    private readonly IAgentDelegationHandler _delegateHandler;

    /// <summary>Creates a DelegateTool backed by a context-aware host handler.</summary>
    public DelegateTool(IAgentDelegationHandler delegateHandler)
    {
        ArgumentNullException.ThrowIfNull(delegateHandler);
        _delegateHandler = delegateHandler;
    }

    public string Name => "delegate";

    /// <summary>
    /// A subagent's final response is a reusable work product. Persist it even when it is small,
    /// so the parent can inspect the full, redacted artifact after context projection.
    /// </summary>
    public ToolResultRetentionPolicy RetentionPolicy { get; } = new()
    {
        HasSideEffects = true,
        PreferPersistence = true,
        MinimumLevel = ToolResultRetentionLevel.Placeholder
    };

    public ToolDefinition Definition => new()
    {
        Name = "delegate",
        Description = "Delegate a task to another agent. Use this when you need help from a specialized agent.",
        Schema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""agent_id"": {
                    ""type"": ""string"",
                    ""description"": ""The ID of the agent to delegate to""
                },
                ""task"": {
                    ""type"": ""string"",
                    ""description"": ""The task description for the agent""
                }
            },
            ""required"": [""agent_id"", ""task""]
        }")
    };

    public async Task<ToolResult> ExecuteAsync(IDictionary<string, object> args, ToolExecutionContext context)
    {
        try
        {
            var arguments = new ToolArgumentReader(Definition.Schema, args);
            var agentId = arguments.GetString("agent_id");
            var task = arguments.GetString("task");

            return await _delegateHandler.DelegateAsync(new AgentDelegationRequest
            {
                AgentId = agentId,
                Task = task,
                ParentContext = context
            }, context.CancellationToken);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Delegation failed: {ex.Message}");
        }
    }

    public ToolResultProjection CreatePreview(ToolResult result, ToolResultProjectionContext context) =>
        DefaultToolResultProjector.Instance.CreatePreview(result, context);

    public ToolResultProjection CreatePlaceholder(ToolResultProjectionContext context) =>
        DefaultToolResultProjector.Instance.CreatePlaceholder(context);

}
