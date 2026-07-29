using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools;

/// <summary>
/// 委托工具 - 将任务委托给其他 Agent
/// </summary>
public class DelegateTool : ITool
{
    private readonly Func<string, string, Task<string>> _delegateHandler;

    /// <summary>
    /// 创建委托工具
    /// </summary>
    /// <param name="delegateHandler">委托处理函数 (agentId, task) => result</param>
    public DelegateTool(Func<string, string, Task<string>> delegateHandler)
    {
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

            var result = await _delegateHandler(agentId, task);
            return ToolResult.FromText($"[Delegated to {agentId}] {result}");
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Delegation failed: {ex.Message}");
        }
    }
}
