using System.Text.Json;

namespace InsightaAI.Agent.Mcp.Local;

/// <summary>
/// 从 JSON 文件读取 MCP 服务器配置
/// </summary>
public class JsonMcpServerProvider : IMcpServerProvider
{
    private readonly string _configPath;

    public string ProviderName => "json";

    public JsonMcpServerProvider(string configPath)
    {
        _configPath = configPath;
    }

    public async Task<IReadOnlyList<McpServerConfig>> GetServersAsync(CancellationToken cancellationToken = default)
    {
        var configFile = await LoadConfigAsync(cancellationToken);
        return configFile.Servers.Values.ToList();
    }

    public async Task<McpServerConfig?> GetServerAsync(string name, CancellationToken cancellationToken = default)
    {
        var configFile = await LoadConfigAsync(cancellationToken);
        return configFile.Servers.TryGetValue(name, out var config) ? config : null;
    }

    public async Task AddServerAsync(McpServerConfig config, CancellationToken cancellationToken = default)
    {
        var configFile = await LoadConfigAsync(cancellationToken);
        configFile.Servers[config.Name] = config;
        await SaveConfigAsync(configFile, cancellationToken);
    }

    public async Task RemoveServerAsync(string name, CancellationToken cancellationToken = default)
    {
        var configFile = await LoadConfigAsync(cancellationToken);
        configFile.Servers.Remove(name);
        await SaveConfigAsync(configFile, cancellationToken);
    }

    private async Task<McpConfigFile> LoadConfigAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_configPath))
        {
            return new McpConfigFile();
        }

        var json = await File.ReadAllTextAsync(_configPath, cancellationToken);
        return JsonSerializer.Deserialize<McpConfigFile>(json, JsonOptions) ?? new McpConfigFile();
    }

    private async Task SaveConfigAsync(McpConfigFile config, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(_configPath, json, cancellationToken);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}
