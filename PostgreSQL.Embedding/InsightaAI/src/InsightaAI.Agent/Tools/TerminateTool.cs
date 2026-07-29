using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools;

/// <summary>
/// 终止工具 - Agent 主动结束并返回最终答案
/// </summary>
public class TerminateTool : ITool
{
    public string Name => "terminate";

    public ToolDefinition Definition => new()
    {
        Name = "terminate",
        Description = "Terminate the agent loop and return the final answer. Use this when you have completed the task.",
        Schema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""answer"": {
                    ""type"": ""string"",
                    ""description"": ""The final answer to return""
                }
            },
            ""required"": [""answer""]
        }")
    };

    public Task<ToolResult> ExecuteAsync(IDictionary<string, object> args, ToolExecutionContext context)
    {
        try
        {
            var arguments = new ToolArgumentReader(Definition.Schema, args);
            var answer = arguments.GetString("answer");
            return Task.FromResult(new ToolResult
            {
                Content = [new TextBlock { Text = $"[TERMINATE] {answer}" }]
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromError(ex.Message));
        }
    }
}

/// <summary>
/// 任务完成工具 - 标记任务完成
/// </summary>
public class CompleteTaskTool : ITool
{
    public string Name => "complete_task";

    public ToolDefinition Definition => new()
    {
        Name = "complete_task",
        Description = "Mark the current task as complete with a summary of what was accomplished.",
        Schema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""summary"": {
                    ""type"": ""string"",
                    ""description"": ""Summary of completed work""
                },
                ""output"": {
                    ""type"": ""string"",
                    ""description"": ""Final output or result""
                }
            },
            ""required"": [""summary""]
        }")
    };

    public Task<ToolResult> ExecuteAsync(IDictionary<string, object> args, ToolExecutionContext context)
    {
        try
        {
            var arguments = new ToolArgumentReader(Definition.Schema, args);
            var summary = arguments.GetString("summary");
            var output = arguments.GetString("output", "");
            var result = string.IsNullOrEmpty(output) ? summary : $"{summary}\n\nOutput:\n{output}";

            return Task.FromResult(new ToolResult
            {
                Content = [new TextBlock { Text = $"[TASK_COMPLETE] {result}" }]
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.FromError(ex.Message));
        }
    }
}
