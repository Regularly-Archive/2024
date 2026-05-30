using InsightaAI.LLM.Models;

namespace InsightaAI.LLM.Abstractions;

/// <summary>
/// 工具执行器接口
/// </summary>
public interface IToolExecutor
{
    /// <summary>工具名称</summary>
    string Name { get; }

    /// <summary>工具定义</summary>
    ToolDefinition Definition { get; }

    /// <summary>执行工具</summary>
    Task<ToolResult> ExecuteAsync(
        IDictionary<string, object> args,
        ToolExecutionContext context);
}
