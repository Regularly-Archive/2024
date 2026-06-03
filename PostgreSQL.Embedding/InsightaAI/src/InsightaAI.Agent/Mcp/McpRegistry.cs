using System.Collections.Concurrent;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using System.Text.Json;

namespace InsightaAI.Agent.Mcp;

/// <summary>
/// MCP 注册表 - 管理 MCP 服务器和工具激活
/// </summary>
public class McpRegistry
{
    private readonly List<IMcpServerProvider> _providers = [];
    private readonly IMcpConnectionPool _connectionPool;
    private readonly ConcurrentDictionary<string, McpServerConfig> _serverCache = new();
    private readonly ConcurrentDictionary<string, McpToolMetadata> _activeTools = new();

    public McpRegistry(IMcpConnectionPool connectionPool)
    {
        _connectionPool = connectionPool;
    }

    /// <summary>注册配置提供者</summary>
    public void RegisterProvider(IMcpServerProvider provider)
    {
        _providers.Add(provider);
    }

    /// <summary>获取所有服务器元数据（用于 SystemPrompt）</summary>
    public async Task<IReadOnlyList<McpServerMetadata>> ListAllServersAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<McpServerMetadata>();

        foreach (var provider in _providers)
        {
            var servers = await provider.GetServersAsync(cancellationToken);
            foreach (var server in servers)
            {
                _serverCache.TryAdd(server.Name, server);
                result.Add(new McpServerMetadata
                {
                    Name = server.Name,
                    Description = server.Description ?? ""
                });
            }
        }

        return result;
    }

    /// <summary>列出指定服务器的工具</summary>
    public async Task<IReadOnlyList<McpToolMetadata>> ListToolsAsync(string serverName, CancellationToken cancellationToken = default)
    {
        var config = await GetServerConfigAsync(serverName, cancellationToken);
        if (config == null)
        {
            return [];
        }

        var tools = await _connectionPool.ListToolsAsync(config, cancellationToken);
        return tools.Select(t => new McpToolMetadata
        {
            Name = t.Name,
            RegisteredName = GetRegisteredName(serverName, t.Name),
            Description = t.Description ?? "",
            ServerName = serverName,
            InputSchema = t.JsonSchema
        }).ToList();
    }

    /// <summary>激活工具（注册到 ToolRegistry）</summary>
    public async Task<McpToolMetadata?> ActivateToolAsync(string serverName, string toolName, ToolRegistry toolRegistry, CancellationToken cancellationToken = default)
    {
        var config = await GetServerConfigAsync(serverName, cancellationToken);
        if (config == null)
        {
            return null;
        }

        var tools = await _connectionPool.ListToolsAsync(config, cancellationToken);
        var tool = tools.FirstOrDefault(t => t.Name == toolName);
        if (tool == null)
        {
            return null;
        }

        var registeredName = GetRegisteredName(serverName, toolName);

        // 解析 schema
        var schema = tool.JsonSchema;

        // 注册到 ToolRegistry
        toolRegistry.RegisterFunction(
            registeredName,
            $"[MCP:{serverName}] {tool.Description}",
            schema,
            async (args, ctx) =>
            {
                var arguments = args.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                var result = await _connectionPool.CallToolAsync(config, toolName, arguments, ctx.CancellationToken);
                return ToolResult.FromText(result);
            });

        var metadata = new McpToolMetadata
        {
            Name = toolName,
            RegisteredName = registeredName,
            Description = tool.Description ?? "",
            ServerName = serverName,
            InputSchema = schema
        };

        _activeTools.TryAdd(registeredName, metadata);
        return metadata;
    }

    /// <summary>停用工具（从 ToolRegistry 移除）</summary>
    public bool DeactivateTool(string registeredName, ToolRegistry toolRegistry)
    {
        if (_activeTools.TryRemove(registeredName, out _))
        {
            toolRegistry.Unregister(registeredName);
            return true;
        }
        return false;
    }

    /// <summary>获取已激活的工具列表</summary>
    public IReadOnlyList<McpToolMetadata> GetActiveTools()
    {
        return _activeTools.Values.ToList();
    }

    /// <summary>检查工具是否已激活</summary>
    public bool IsToolActive(string registeredName)
    {
        return _activeTools.ContainsKey(registeredName);
    }

    private async Task<McpServerConfig?> GetServerConfigAsync(string serverName, CancellationToken cancellationToken)
    {
        if (_serverCache.TryGetValue(serverName, out var cached))
        {
            return cached;
        }

        foreach (var provider in _providers)
        {
            var config = await provider.GetServerAsync(serverName, cancellationToken);
            if (config != null)
            {
                _serverCache.TryAdd(serverName, config);
                return config;
            }
        }

        return null;
    }

    private static string GetRegisteredName(string serverName, string toolName)
    {
        return $"mcp__{serverName}__{toolName}";
    }
}
