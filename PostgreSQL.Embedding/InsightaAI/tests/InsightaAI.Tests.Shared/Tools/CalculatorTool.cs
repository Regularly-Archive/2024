using System.Text.Json;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Tests.Shared;

/// <summary>
/// 简单的计算器工具 (用于测试)
/// </summary>
public class CalculatorTool : IToolExecutor
{
    public string Name => "calculator";

    public ToolDefinition Definition => new()
    {
        Name = "calculator",
        Description = "Perform basic arithmetic operations",
        Schema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""operation"": {
                    ""type"": ""string"",
                    ""enum"": [""add"", ""subtract"", ""multiply"", ""divide""],
                    ""description"": ""The arithmetic operation to perform""
                },
                ""a"": {
                    ""type"": ""number"",
                    ""description"": ""First operand""
                },
                ""b"": {
                    ""type"": ""number"",
                    ""description"": ""Second operand""
                }
            },
            ""required"": [""operation"", ""a"", ""b""]
        }")
    };

    public Task<ToolResult> ExecuteAsync(IDictionary<string, object> args, ToolExecutionContext context)
    {
        var operation = args["operation"]?.ToString();
        var a = Convert.ToDouble(args["a"]);
        var b = Convert.ToDouble(args["b"]);

        var result = operation switch
        {
            "add" => a + b,
            "subtract" => a - b,
            "multiply" => a * b,
            "divide" => b != 0 ? a / b : double.NaN,
            _ => throw new ArgumentException($"Unknown operation: {operation}")
        };

        return Task.FromResult(ToolResult.FromText(result.ToString()));
    }
}
