namespace InsightaAI.Agent.Mcp;

/// <summary>
/// MCP 工具调用结果，带 Metadata 用于遥测层消费
/// </summary>
public sealed record McpToolCallResult
{
    /// <summary>工具返回的文本内容</summary>
    public required string Text { get; init; }

    /// <summary>是否为错误</summary>
    public bool IsError { get; init; }

    /// <summary>连接池层产出的元数据（如 serverInfo、protocol version 等）</summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
}
