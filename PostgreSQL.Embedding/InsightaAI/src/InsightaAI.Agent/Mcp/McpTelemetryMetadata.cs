namespace InsightaAI.Agent.Mcp;

/// <summary>
/// Builds telemetry attributes whose values originate from local MCP configuration.
/// Remote server identity is deliberately produced by the connection pool instead.
/// </summary>
public static class McpTelemetryMetadata
{
    /// <summary>
    /// Combines remote handshake attributes with the local configuration attributes
    /// needed for one MCP tool invocation.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> ForToolCall(
        McpServerConfig config,
        string toolName,
        IReadOnlyDictionary<string, object?>? serverMetadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        var metadata = new Dictionary<string, object?>(
            serverMetadata ?? new Dictionary<string, object?>())
        {
            ["mcp.method.name"] = toolName,
        };

        foreach (var entry in FromConfig(config))
            metadata[entry.Key] = entry.Value;

        return metadata;
    }

    public static IReadOnlyDictionary<string, object?> FromConfig(McpServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var metadata = new Dictionary<string, object?>
        {
            ["mcp.config.name"] = config.Name,
            ["mcp.config.description"] = config.Description,
            ["mcp.config.transport"] = config.Transport,
        };

        var endpoint = NormalizeEndpoint(config.Endpoint);
        if (endpoint is not null)
            metadata["mcp.config.endpoint"] = endpoint;

        return metadata;
    }

    /// <summary>
    /// Retains only the endpoint origin, preventing credentials, paths and query values
    /// from becoming telemetry attributes.
    /// </summary>
    public static string? NormalizeEndpoint(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            string.IsNullOrEmpty(uri.Scheme) || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        var authority = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        return $"{uri.Scheme}://{authority}";
    }
}
