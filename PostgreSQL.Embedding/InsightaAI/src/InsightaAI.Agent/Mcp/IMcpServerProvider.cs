namespace InsightaAI.Agent.Mcp;

/// <summary>
/// MCP 服务器配置提供者接口
/// </summary>
public interface IMcpServerProvider
{
    /// <summary>提供者名称</summary>
    string ProviderName { get; }

    /// <summary>获取所有服务器配置</summary>
    Task<IReadOnlyList<McpServerConfig>> GetServersAsync(CancellationToken cancellationToken = default);

    /// <summary>获取指定服务器配置</summary>
    Task<McpServerConfig?> GetServerAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>添加服务器配置</summary>
    Task AddServerAsync(McpServerConfig config, CancellationToken cancellationToken = default);

    /// <summary>移除服务器配置</summary>
    Task RemoveServerAsync(string name, CancellationToken cancellationToken = default);
}
