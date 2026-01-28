using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Domain.Entities;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Newtonsoft.Json;

namespace PostgreSQL.Embedding.Common.Extensions
{

    /// <summary>
    /// MCP 连接包装类，包含连接健康状态和工具缓存
    /// </summary>
    public class McpConnection : IDisposable
    {
        public IMcpClient Client { get; }
        public string ServerKey { get; }
        public DateTime CreatedAt { get; }
        public DateTime LastUsedAt { get; private set; }
        public bool IsHealthy { get; set; } = true;
        private readonly ILogger _logger;

        private IList<McpClientTool>? _cachedTools;

        public McpConnection(string serverKey, IMcpClient client, ILogger logger)
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
                _logger.LogDebug("Disposed MCP client for server {ServerKey}", ServerKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing MCP client for server {ServerKey}", ServerKey);
            }
        }
    }

    /// <summary>
    /// 工具列表缓存项
    /// </summary>
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

    /// <summary>
    /// 增强版 MCP 客户端工厂，支持连接池、TTL 和工具缓存
    /// </summary>
    public class CacheableMcpClientFactory : IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CacheableMcpClientFactory> _logger;

        // 连接池：带 TTL 和健康检查
        private readonly ConcurrentDictionary<string, McpConnection> _connections = new();

        // 工具列表缓存（跨连接复用）
        private readonly ConcurrentDictionary<string, ToolCacheItem> _toolCache = new();

        // 配置
        private readonly TimeSpan _connectionTTL = TimeSpan.FromMinutes(10);
        private readonly TimeSpan _toolCacheTTL = TimeSpan.FromMinutes(30);
        private readonly int _maxConnections = 50;
        private readonly int _maxRetryCount = 3;
        private readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(500);

        public CacheableMcpClientFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger<CacheableMcpClientFactory>()
                        ?? throw new InvalidOperationException("ILoggerFactory is not registered");

            // 启动后台清理任务
            _ = Task.Run(BackgroundCleanupAsync);
        }

        /// <summary>
        /// 获取或创建连接（带重试机制）- 返回 IMcpClient
        /// </summary>
        public IMcpClient GetOrCreate(string name, string command, string[] args = null, Dictionary<string, string> env = null)
        {
            var validName = name.Replace("-", "_");

            // 创建临时服务器对象用于 GetOrCreate
            var tempServer = new MCPServer
            {
                Name = name,
                Command = command,
                Arguments = args,
                EnvVars = env,
                TransportType = (int)TransportType.Stdio
            };

            return GetOrCreate(validName, tempServer).Client;
        }

        /// <summary>
        /// 获取或创建连接（带重试机制）- 返回 IMcpClient
        /// </summary>
        public IMcpClient GetOrCreate(string name, string url, Dictionary<string, string> headers = null)
        {
            var validName = name.Replace("-", "_");

            // 创建临时服务器对象用于 GetOrCreate
            var tempServer = new MCPServer
            {
                Name = name,
                Endpoint = url,
                ExtraHeaders = headers ?? new Dictionary<string, string>(),
                TransportType = (int)TransportType.Http
            };

            return GetOrCreate(validName, tempServer).Client;
        }

        /// <summary>
        /// 获取或创建连接（返回 McpConnection）
        /// </summary>
        public McpConnection GetOrCreate(MCPServer server)
        {
            var serverKey = GetServerKey(server);
            return GetOrCreate(serverKey, server);
        }

        /// <summary>
        /// 获取或创建连接（带重试机制）
        /// </summary>
        public McpConnection GetOrCreate(string serverKey, MCPServer server)
        {
            return _connections.GetOrAdd(serverKey, key =>
            {
                _logger.LogInformation("Creating new MCP connection for server: {ServerKey}", key);

                var client = CreateClientWithRetry(server);
                var connection = new McpConnection(key, client, _logger);

                // 尝试恢复工具缓存
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

        /// <summary>
        /// 获取工具列表（优先从缓存获取）
        /// </summary>
        public async Task<IList<McpClientTool>> GetToolsAsync(MCPServer server, bool forceRefresh = false)
        {
            var serverKey = GetServerKey(server);

            // 尝试从工具缓存获取
            if (!forceRefresh && _toolCache.TryGetValue(serverKey, out var cachedItem))
            {
                if (cachedItem.CachedAt.Add(_toolCacheTTL) > DateTime.UtcNow)
                {
                    _logger.LogDebug("Using cached tools for server {ServerKey}, cached {Time} ago",
                        serverKey, DateTime.UtcNow - cachedItem.CachedAt);
                    return cachedItem.Tools;
                }
            }

            // 从连接获取并缓存
            var connection = GetOrCreate(serverKey, server);
            var tools = await connection.GetToolsAsync(forceRefresh);

            // 更新缓存
            _toolCache.AddOrUpdate(serverKey,
                new ToolCacheItem(tools),
                (_, existing) => new ToolCacheItem(tools));

            return tools;
        }

        /// <summary>
        /// 获取工具列表（同步版本，带缓存）
        /// </summary>
        public IList<McpClientTool> GetTools(MCPServer server, bool forceRefresh = false)
        {
            var serverKey = GetServerKey(server);

            // 尝试从工具缓存获取
            if (!forceRefresh && _toolCache.TryGetValue(serverKey, out var cachedItem))
            {
                if (cachedItem.CachedAt.Add(_toolCacheTTL) > DateTime.UtcNow)
                {
                    return cachedItem.Tools;
                }
            }

            // 从连接获取
            var connection = GetOrCreate(server);
            var tools = connection.GetTools(forceRefresh);

            // 更新缓存
            _toolCache.AddOrUpdate(serverKey,
                new ToolCacheItem(tools),
                (_, existing) => new ToolCacheItem(tools));

            return tools;
        }

        /// <summary>
        /// 调用工具（带超时和重试）
        /// </summary>
        public async Task<string> CallToolAsync(MCPServer server, string toolName, Dictionary<string, object> arguments)
        {
            var connection = GetOrCreate(server);
            connection.Touch();

            var result = await ExecuteWithRetryAsync(async () =>
            {
                var callResult = await connection.Client.CallToolAsync(toolName, arguments);
                return string.Join("\n", callResult.Content.Where(c => c.Type == "text").Select(c => c.Text));
            });

            return result;
        }

        /// <summary>
        /// 刷新服务器的工具缓存
        /// </summary>
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

        /// <summary>
        /// 检查连接健康状态
        /// </summary>
        public bool IsHealthy(MCPServer server)
        {
            var serverKey = GetServerKey(server);
            return _connections.TryGetValue(serverKey, out var connection) && connection.IsHealthy;
        }

        /// <summary>
        /// 移除指定服务器连接
        /// </summary>
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

        /// <summary>
        /// 清除所有连接和缓存
        /// </summary>
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

        /// <summary>
        /// 获取当前连接统计
        /// </summary>
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

        private IMcpClient CreateClientWithRetry(MCPServer server, int? maxRetryCount = null)
        {
            maxRetryCount ??= _maxRetryCount;
            var attempt = 0;
            var delay = _retryDelay;

            while (attempt < maxRetryCount)
            {
                try
                {
                    attempt++;
                    _logger.LogDebug("Attempt {Attempt}/{MaxRetry} to create MCP client for server {ServerName}",
                        attempt, maxRetryCount, server.Name);

                    var stopwatch = Stopwatch.StartNew();

                    IMcpClient client = server.TransportType == (int)TransportType.Stdio
                        ? CreateStdioClient(server)
                        : CreateHttpClient(server);

                    stopwatch.Stop();
                    _logger.LogInformation("Created MCP client for server {ServerName} in {ElapsedMs}ms (attempt {Attempt})",
                        server.Name, stopwatch.ElapsedMilliseconds, attempt);

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

        private IMcpClient CreateStdioClient(MCPServer server)
        {
            var validName = server.Name.Replace("-", "_");
            var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = validName,
                Command = server.Command,
                Arguments = server.Arguments,
                EnvironmentVariables = server.EnvVars
            });

            var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
            return McpClientFactory.CreateAsync(clientTransport, loggerFactory: loggerFactory).Result;
        }

        private IMcpClient CreateHttpClient(MCPServer server)
        {
            var validName = server.Name.Replace("-", "_");
            var headers = server.ExtraHeaders ?? new Dictionary<string, string>();

            var clientTransport = new SseClientTransport(new SseClientTransportOptions
            {
                Name = validName,
                Endpoint = new Uri(server.Endpoint),
                AdditionalHeaders = headers
            });

            var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
            return McpClientFactory.CreateAsync(clientTransport, loggerFactory: loggerFactory).Result;
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
                            _logger.LogDebug("Cleaned up expired connection for server {ServerKey}, idle for {IdleTime}",
                                key, now - conn.LastUsedAt);
                        }
                    }

                    // 清理过期工具缓存
                    var expiredTools = _toolCache
                        .Where(kvp => now - kvp.Value.CachedAt > _toolCacheTTL)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in expiredTools)
                    {
                        _toolCache.TryRemove(key, out _);
                        _logger.LogDebug("Cleaned up expired tool cache for server {ServerKey}", key);
                    }

                    // 限制连接数量
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
