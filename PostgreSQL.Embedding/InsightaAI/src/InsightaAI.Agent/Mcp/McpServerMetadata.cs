namespace InsightaAI.Agent.Mcp;

/// <summary>
/// MCP 服务器轻量元数据（用于 SystemPrompt）
/// </summary>
public record McpServerMetadata
{
    /// <summary>服务器名称</summary>
    public required string Name { get; init; }

    /// <summary>服务器描述</summary>
    public string Description { get; init; } = "";

    /// <summary>可用工具数量</summary>
    public int ToolCount { get; init; }
}

/// <summary>
/// MCP 工具元数据
/// </summary>
public record McpToolMetadata
{
    /// <summary>工具名称（原始）</summary>
    public required string Name { get; init; }

    /// <summary>注册到 Agent 的名称（带前缀）</summary>
    public required string RegisteredName { get; init; }

    /// <summary>工具描述</summary>
    public string Description { get; init; } = "";

    /// <summary>所属服务器名称</summary>
    public required string ServerName { get; init; }

    /// <summary>输入参数 Schema</summary>
    public object? InputSchema { get; init; }
}
