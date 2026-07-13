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
        var answer = args.TryGetValue("answer", out var val) ? val?.ToString() ?? "" : "";

        return Task.FromResult(new ToolResult
        {
            Content = [new TextBlock { Text = $"[TERMINATE] {answer}" }]
        });
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
        var summary = args.TryGetValue("summary", out var s) ? s?.ToString() ?? "" : "";
        var output = args.TryGetValue("output", out var o) ? o?.ToString() ?? "" : "";

        var result = string.IsNullOrEmpty(output) ? summary : $"{summary}\n\nOutput:\n{output}";

        return Task.FromResult(new ToolResult
        {
            Content = [new TextBlock { Text = $"[TASK_COMPLETE] {result}" }]
        });
    }
}
