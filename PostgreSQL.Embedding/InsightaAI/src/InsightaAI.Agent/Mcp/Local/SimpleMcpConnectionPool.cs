using ModelContextProtocol.Client;
using System.Collections.Concurrent;

namespace InsightaAI.Agent.Mcp.Local;

/// <summary>
/// 简单的 MCP 连接池实现（CLI 场景）
/// 按需连接，无后台清理线程
/// </summary>
public class SimpleMcpConnectionPool : IMcpConnectionPool
{
    private readonly ConcurrentDictionary<string, Lazy<Task<McpClient>>> _connections = new();
    private readonly ConcurrentDictionary<string, IList<McpClientTool>> _toolCache = new();

    public async Task<McpClient> GetConnectionAsync(McpServerConfig config, CancellationToken cancellationToken = default)
    {
        var lazyTask = _connections.GetOrAdd(config.Name, _ => new Lazy<Task<McpClient>>(() => CreateClientAsync(config)));
        return await lazyTask.Value;
    }

    public async Task<IList<McpClientTool>> ListToolsAsync(McpServerConfig config, CancellationToken cancellationToken = default)
    {
        if (_toolCache.TryGetValue(config.Name, out var cached))
        {
            return cached;
        }

        var client = await GetConnectionAsync(config, cancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        _toolCache[config.Name] = tools;
        return tools;
    }

    public async Task<string> CallToolAsync(McpServerConfig config, string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        var client = await GetConnectionAsync(config, cancellationToken);
        var args = arguments.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value);
        var result = await client.CallToolAsync(toolName, args, cancellationToken: cancellationToken);
        return string.Join("\n", result.Content.Where(c => c.Type == "text").Select(c => c.ToString()));
    }

    public async Task RemoveAsync(string serverName)
    {
        if (_connections.TryRemove(serverName, out var lazyTask))
        {
            if (lazyTask.IsValueCreated && lazyTask.Value.IsCompletedSuccessfully)
            {
                await lazyTask.Value.Result.DisposeAsync();
            }
        }
        _toolCache.TryRemove(serverName, out _);
    }

    public async Task ClearAsync()
    {
        foreach (var kvp in _connections)
        {
            if (kvp.Value.IsValueCreated && kvp.Value.Value.IsCompletedSuccessfully)
            {
                await kvp.Value.Value.Result.DisposeAsync();
            }
        }
        _connections.Clear();
        _toolCache.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await ClearAsync();
    }

    private static async Task<McpClient> CreateClientAsync(McpServerConfig config)
    {
        IClientTransport transport = config.Transport.ToLower() switch
        {
            "stdio" => CreateStdioTransport(config),
            "http" or "sse" => CreateHttpTransport(config),
            _ => throw new ArgumentException($"Unsupported transport type: {config.Transport}")
        };

        return await McpClient.CreateAsync(transport);
    }

    private static IClientTransport CreateStdioTransport(McpServerConfig config)
    {
        if (string.IsNullOrEmpty(config.Command))
        {
            throw new ArgumentException("Command is required for stdio transport");
        }

        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = config.Name,
            Command = config.Command,
            Arguments = config.Args ?? [],
            EnvironmentVariables = config.Env
        });
    }

    private static IClientTransport CreateHttpTransport(McpServerConfig config)
    {
        if (string.IsNullOrEmpty(config.Endpoint))
        {
            throw new ArgumentException("Endpoint is required for http transport");
        }

        return new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = config.Name,
            Endpoint = new Uri(config.Endpoint),
            AdditionalHeaders = config.Headers
        });
    }
}
