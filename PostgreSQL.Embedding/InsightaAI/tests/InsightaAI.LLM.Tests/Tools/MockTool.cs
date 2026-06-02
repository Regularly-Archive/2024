using System.Text.Json;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.LLM.Tests.Tools;

/// <summary>
/// 简单的 Mock 工具 (用于测试)
/// </summary>
public class MockTool : IToolExecutor
{
    public string Name { get; }

    public ToolDefinition Definition => new()
    {
        Name = Name,
        Description = $"Mock tool: {Name}",
        Schema = JsonSerializer.Deserialize<JsonElement>("{\"type\": \"object\"}")
    };

    public MockTool(string name)
    {
        Name = name;
    }

    public Task<ToolResult> ExecuteAsync(IDictionary<string, object> args, ToolExecutionContext context)
    {
        return Task.FromResult(ToolResult.FromText($"Executed {Name}"));
    }
}
