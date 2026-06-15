using System.Text.Json;

namespace InsightaAI.LLM.Models;

/// <summary>
/// 工具定义
/// </summary>
public sealed record ToolDefinition
{
    /// <summary>工具名称</summary>
    public required string Name { get; init; }

    /// <summary>工具描述</summary>
    public required string Description { get; init; }

    /// <summary>工具参数 JSON Schema</summary>
    public required JsonElement Schema { get; init; }
}
