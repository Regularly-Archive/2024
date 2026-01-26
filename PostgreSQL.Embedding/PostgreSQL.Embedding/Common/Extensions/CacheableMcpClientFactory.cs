using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Domain.Entities;
using System.Collections.Concurrent;

namespace PostgreSQL.Embedding.Common.Extensions
{
    public class CacheableMcpClientFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<string, IMcpClient> _cachedClients = new ConcurrentDictionary<string, IMcpClient>();

        public CacheableMcpClientFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IMcpClient GetOrCreate(string name, string command, string[] args = null, Dictionary<string, string> env = null)
        {
            var validName = name.Replace("-", "_");

            return _cachedClients.GetOrAdd(validName, _ =>
            {
                var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = validName,
                    Command = command,
                    Arguments = args ?? [],
                    EnvironmentVariables = env ?? new Dictionary<string, string>()
                });

                var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
                var client = McpClientFactory.CreateAsync(clientTransport, loggerFactory: loggerFactory).Result;
                return client;
            });
        }

        public IMcpClient GetOrCreate(string name, string url, Dictionary<string, string> headers = null)
        {
            var validName = name.Replace("-", "_");

            return _cachedClients.GetOrAdd(validName, _ =>
            {
                var clientTransport = new SseClientTransport(new SseClientTransportOptions
                {
                    Name = validName,
                    Endpoint = new Uri(url),
                    AdditionalHeaders = headers ?? new Dictionary<string, string>()
                });

                var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
                return McpClientFactory.CreateAsync(clientTransport, loggerFactory: loggerFactory).Result;
            });
        }

        public IMcpClient GetOrCreate(MCPServer server)
        {
            return server.TransportType == (int)TransportType.Stdio
                ? GetOrCreate(server.Name, server.Command, server.Arguments, server.EnvVars)
                : GetOrCreate(server.Name, server.Endpoint, server.ExtraHeaders);
        }

        public bool TryGet(string key, out IMcpClient client)
        {
            return _cachedClients.TryGetValue(key, out client);
        }

        public void Remove(string key)
        {
            _cachedClients.TryRemove(key, out _);
        }

        public void Clear()
        {
            _cachedClients.Clear();
        }
    }
}
