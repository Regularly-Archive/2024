using ModelContextProtocol.Client;
using PostgreSQL.Embedding.Domain.Entities;
using System.Collections.Concurrent;

namespace PostgreSQL.Embedding.Common.Extensions
{
    public class McpConnection : IDisposable
    {
        public McpClient Client { get; }
        public string ServerKey { get; }
        public DateTime CreatedAt { get; }
        public DateTime LastUsedAt { get; private set; }
        public bool IsHealthy { get; set; } = true;
        private readonly ILogger _logger;

        private IList<McpClientTool>? _cachedTools;

        public McpConnection(string serverKey, McpClient client, ILogger logger)
        {
            ServerKey = serverKey;
            Client = client;
            CreatedAt = DateTime.UtcNow;
            LastUsedAt = DateTime.UtcNow;
            _logger = logger;
        }

        public IList<McpClientTool> GetTools(bool forceRefresh = false)
        {
            if (_cachedTools != null && !forceRefresh) return _cachedTools;

            try
            {
                _cachedTools = Client.ListToolsAsync().GetAwaiter().GetResult();
                _logger.LogDebug("Cached {Count} tools for server {ServerKey}", _cachedTools.Count, ServerKey);
                return _cachedTools;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get tools from server {ServerKey}", ServerKey);
                return _cachedTools ?? new List<McpClientTool>();
            }
        }

        public async Task<IList<McpClientTool>> GetToolsAsync(bool forceRefresh = false)
        {
            if (_cachedTools != null && !forceRefresh) return _cachedTools;

            try
            {
                _cachedTools = await Client.ListToolsAsync();
                _logger.LogDebug("Cached {Count} tools for server {ServerKey}", _cachedTools.Count, ServerKey);
                return _cachedTools;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get tools from server {ServerKey}", ServerKey);
                return _cachedTools ?? new List<McpClientTool>();
            }
        }

        public async Task<IList<McpClientResource>> GetResourcesAsync()
        {
            var resources = await Client.ListResourcesAsync();
            return resources;
        }

        public void Touch()
        {
            LastUsedAt = DateTime.UtcNow;
        }

        internal void RestoreCachedTools(IList<McpClientTool> tools)
        {
            _cachedTools = tools;
        }

        public void Dispose()
        {
            try
            {
                Client.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing MCP client for server {ServerKey}", ServerKey);
            }
        }
    }

    public class ToolCacheItem
    {
        public IList<McpClientTool> Tools { get; }
        public DateTime CachedAt { get; }
        public string ServerVersion { get; }

        public ToolCacheItem(IList<McpClientTool> tools, string serverVersion = "")
        {
            Tools = tools;
            CachedAt = DateTime.UtcNow;
            ServerVersion = serverVersion;
        }
    }

    public class McpConnectionFactory : IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<McpConnectionFactory> _logger;

        private readonly ConcurrentDictionary<string, McpConnection> _connections = new();
        private readonly ConcurrentDictionary<string, ToolCacheItem> _toolCache = new();

        private readonly TimeSpan _connectionTTL = TimeSpan.FromMinutes(10);
        private readonly TimeSpan _toolCacheTTL = TimeSpan.FromMinutes(30);

        private readonly int _maxConnections = 50;
        private readonly int _maxRetryCount = 3;
        private readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(500);

        public McpConnectionFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger<McpConnectionFactory>()
                        ?? throw new InvalidOperationException("ILoggerFactory is not registered");

            _ = Task.Run(BackgroundCleanupAsync);
        }

        public McpConnection GetOrCreate(MCPServer server)
        {
            var serverKey = GetServerKey(server);
            return GetOrCreate(serverKey, server);
        }

        public McpConnection GetOrCreate(string serverKey, MCPServer server)
        {
            return _connections.GetOrAdd(serverKey, key =>
            {
                var client = CreateClient(server);
                var connection = new McpConnection(key, client, _logger);

                if (_toolCache.TryGetValue(key, out var cachedItem) &&
                    cachedItem.CachedAt.Add(_toolCacheTTL) > DateTime.UtcNow)
                {
                    connection.RestoreCachedTools(cachedItem.Tools);
                    _logger.LogDebug("Restored {Count} tools from cache for server {ServerKey}",
                        cachedItem.Tools.Count, key);
                }

                return connection;
            });
        }

        public async Task<IList<McpClientTool>> GetToolsAsync(MCPServer server, bool forceRefresh = false)
        {
            var serverKey = GetServerKey(server);

            if (!forceRefresh && _toolCache.TryGetValue(serverKey, out var cachedItem))
            {
                if (cachedItem.CachedAt.Add(_toolCacheTTL) > DateTime.UtcNow)
                {
                    _logger.LogDebug("Using cached tools for server {ServerKey}, cached {Time} ago",
                        serverKey, DateTime.UtcNow - cachedItem.CachedAt);
                    return cachedItem.Tools;
                }
            }


            var connection = GetOrCreate(serverKey, server);
            var tools = await connection.GetToolsAsync(forceRefresh);

            _toolCache.AddOrUpdate(serverKey,
                new ToolCacheItem(tools),
                (_, existing) => new ToolCacheItem(tools));

            return tools;
        }

        public IList<McpClientTool> GetTools(MCPServer server, bool forceRefresh = false)
        {
            var serverKey = GetServerKey(server);

            if (!forceRefresh && _toolCache.TryGetValue(serverKey, out var cachedItem))
            {
                if (cachedItem.CachedAt.Add(_toolCacheTTL) > DateTime.UtcNow)
                {
                    return cachedItem.Tools;
                }
            }

            var connection = GetOrCreate(server);
            var tools = connection.GetTools(forceRefresh);

            _toolCache.AddOrUpdate(serverKey,
                new ToolCacheItem(tools),
                (_, existing) => new ToolCacheItem(tools));

            return tools;
        }

        public async Task<string> CallToolAsync(MCPServer server, string toolName, Dictionary<string, object> arguments)
        {
            var connection = GetOrCreate(server);
            connection.Touch();

            var result = await ExecuteWithRetryAsync(async () =>
            {
                var callResult = await connection.Client.CallToolAsync(toolName, arguments);
                return string.Join("\n", callResult.Content.Where(c => c.Type == "text").Select(c => c.ToString()));
            });

            return result;
        }

        public async Task RefreshToolCacheAsync(MCPServer server)
        {
            var serverKey = GetServerKey(server);
            _toolCache.TryRemove(serverKey, out _);

            var connection = GetOrCreate(serverKey, server);
            var tools = await connection.GetToolsAsync(forceRefresh: true);

            _toolCache.AddOrUpdate(serverKey,
                new ToolCacheItem(tools),
                (_, _) => new ToolCacheItem(tools));

            _logger.LogInformation("Refreshed tool cache for server {ServerKey}, {Count} tools", serverKey, tools.Count);
        }

        public bool IsHealthy(MCPServer server)
        {
            var serverKey = GetServerKey(server);
            return _connections.TryGetValue(serverKey, out var connection) && connection.IsHealthy;
        }

        public void Remove(MCPServer server)
        {
            var serverKey = GetServerKey(server);
            RemoveByKey(serverKey);
        }

        public void RemoveByKey(string serverKey)
        {
            if (_connections.TryRemove(serverKey, out var connection))
            {
                connection.Dispose();
                _logger.LogInformation("Removed MCP connection for server: {ServerKey}", serverKey);
            }
            _toolCache.TryRemove(serverKey, out _);
        }

        public void Clear()
        {
            foreach (var kvp in _connections)
            {
                kvp.Value.Dispose();
            }
            _connections.Clear();
            _toolCache.Clear();
            _logger.LogInformation("Cleared all MCP connections and tool cache");
        }

        public (int ConnectionCount, int ToolCacheCount, Dictionary<string, TimeSpan> ConnectionAges) GetStats()
        {
            var ages = _connections.ToDictionary(
                kvp => kvp.Key,
                kvp => DateTime.UtcNow - kvp.Value.CreatedAt
            );
            return (_connections.Count, _toolCache.Count, ages);
        }

        #region Private Methods

        private string GetServerKey(MCPServer server)
        {
            return $"{server.AppId}_{server.Name}".Replace("-", "_");
        }

        private McpClient CreateClient(MCPServer server, int? maxRetryCount = null)
        {
            maxRetryCount ??= _maxRetryCount;
            var attempt = 0;
            var delay = _retryDelay;

            while (attempt < maxRetryCount)
            {
                try
                {
                    attempt++;

                    var client = server.TransportType == (int)TransportType.Stdio
                        ? CreateClientWithStdioTransport(server)
                        : CreateClientWithHttpTransport(server);

                    return client;
                }
                catch (Exception ex) when (attempt < maxRetryCount)
                {
                    _logger.LogWarning(ex, "Failed to create MCP client (attempt {Attempt}/{MaxRetry}), retrying in {Delay}ms",
                        attempt, maxRetryCount, delay.TotalMilliseconds);

                    Thread.Sleep(delay);
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2); // 指数退避
                }
            }

            throw new InvalidOperationException($"Failed to create MCP client after {maxRetryCount} attempts");
        }

        private McpClient CreateClientWithStdioTransport(MCPServer server)
        {
            var validName = server.Name.Replace("-", "_");
            var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = validName,
                Command = server.Command,
                Arguments = server.Arguments,
                EnvironmentVariables = server.EnvVars,
            });

            var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
            return McpClient.CreateAsync(clientTransport, loggerFactory: loggerFactory).Result;
        }

        private McpClient CreateClientWithHttpTransport(MCPServer server)
        {
            var validName = server.Name.Replace("-", "_");
            var headers = server.ExtraHeaders ?? new Dictionary<string, string>();

            var clientTransport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Name = validName,
                Endpoint = new Uri(server.Endpoint),
                AdditionalHeaders = headers
            });

            var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
            return McpClient.CreateAsync(clientTransport, loggerFactory: loggerFactory).Result;
        }

        private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation)
        {
            var attempt = 0;
            var delay = _retryDelay;

            while (true)
            {
                try
                {
                    attempt++;
                    return await operation();
                }
                catch (Exception ex) when (attempt < _maxRetryCount)
                {
                    _logger.LogWarning(ex, "Operation failed (attempt {Attempt}/{MaxRetry}), retrying in {Delay}ms",
                        attempt, _maxRetryCount, delay.TotalMilliseconds);
                    await Task.Delay(delay);
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                }
            }
        }

        private async Task BackgroundCleanupAsync()
        {
            while (!Disposed)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1));

                    var now = DateTime.UtcNow;
                    var expiredConnections = _connections
                        .Where(kvp => now - kvp.Value.LastUsedAt > _connectionTTL)
                        .ToList();

                    foreach (var (key, connection) in expiredConnections)
                    {
                        if (_connections.TryRemove(key, out var conn))
                        {
                            conn.Dispose();
                        }
                    }

                    var expiredTools = _toolCache
                        .Where(kvp => now - kvp.Value.CachedAt > _toolCacheTTL)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in expiredTools)
                    {
                        _toolCache.TryRemove(key, out _);
                    }

                    while (_connections.Count > _maxConnections)
                    {
                        var oldestKey = _connections
                            .OrderBy(kvp => kvp.Value.LastUsedAt)
                            .First().Key;

                        RemoveByKey(oldestKey);
                        _logger.LogWarning("Exceeded max connections, removed oldest: {ServerKey}", oldestKey);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during background cleanup");
                }
            }
        }

        private bool Disposed = false;

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            Clear();
        }

        #endregion
    }
}
