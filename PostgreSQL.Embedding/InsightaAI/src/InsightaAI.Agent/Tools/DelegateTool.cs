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
public class DelegateTool : ITool
{
    private readonly IAgentDelegationHandler _delegateHandler;

    /// <summary>
    /// 创建委托工具
    /// </summary>
    /// <param name="delegateHandler">委托处理函数 (agentId, task) => result</param>
    public DelegateTool(Func<string, string, Task<string>> delegateHandler)
    {
        ArgumentNullException.ThrowIfNull(delegateHandler);
        _delegateHandler = new LegacyDelegationHandler(delegateHandler);
    }

    /// <summary>Creates a DelegateTool backed by a context-aware host handler.</summary>
    public DelegateTool(IAgentDelegationHandler delegateHandler)
    {
        ArgumentNullException.ThrowIfNull(delegateHandler);
        _delegateHandler = delegateHandler;
    }

    public string Name => "delegate";

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

    private sealed class LegacyDelegationHandler(Func<string, string, Task<string>> handler) : IAgentDelegationHandler
    {
        public async Task<ToolResult> DelegateAsync(AgentDelegationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await handler(request.AgentId, request.Task);
            return ToolResult.FromText($"[Delegated to {request.AgentId}] {result}");
        }
    }
}
