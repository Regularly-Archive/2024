using ModelContextProtocol.Client;

namespace InsightaAI.Agent.Mcp;

/// <summary>
/// MCP 连接池接口
/// </summary>
public interface IMcpConnectionPool : IAsyncDisposable
{
    /// <summary>获取或创建连接</summary>
    Task<McpClient> GetConnectionAsync(McpServerConfig config, CancellationToken cancellationToken = default);

    /// <summary>列出服务器的工具</summary>
    Task<IList<McpClientTool>> ListToolsAsync(McpServerConfig config, CancellationToken cancellationToken = default);

    /// <summary>调用工具</summary>
    Task<string> CallToolAsync(McpServerConfig config, string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default);

    /// <summary>移除连接</summary>
    Task RemoveAsync(string serverName);

    /// <summary>清除所有连接</summary>
    Task ClearAsync();
}
