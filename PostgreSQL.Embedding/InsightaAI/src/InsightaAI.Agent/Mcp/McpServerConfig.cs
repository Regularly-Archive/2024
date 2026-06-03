namespace InsightaAI.Agent.Mcp;

/// <summary>
/// MCP 服务器配置
/// </summary>
public record McpServerConfig
{
    /// <summary>服务器名称（唯一标识）</summary>
    public required string Name { get; init; }

    /// <summary>服务器描述</summary>
    public string? Description { get; init; }

    /// <summary>传输类型：stdio 或 http</summary>
    public required string Transport { get; init; }

    /// <summary>Stdio 模式：命令路径</summary>
    public string? Command { get; init; }

    /// <summary>Stdio 模式：命令参数</summary>
    public string[]? Args { get; init; }

    /// <summary>Stdio 模式：环境变量</summary>
    public Dictionary<string, string>? Env { get; init; }

    /// <summary>HTTP 模式：端点 URL</summary>
    public string? Endpoint { get; init; }

    /// <summary>HTTP 模式：请求头</summary>
    public Dictionary<string, string>? Headers { get; init; }
}

/// <summary>
/// MCP 配置文件结构
/// </summary>
public record McpConfigFile
{
    /// <summary>服务器配置字典</summary>
    public Dictionary<string, McpServerConfig> Servers { get; init; } = [];
}
