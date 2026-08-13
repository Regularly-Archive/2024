using InsightaAI.Agent.Mcp;

namespace InsightaAI.Agent.Tests.Mcp;

public sealed class McpTelemetryMetadataTests
{
    [Fact]
    public void FromConfig_UsesConfigNamespace_AndDoesNotExposeLegacyClientAttributes()
    {
        var metadata = McpTelemetryMetadata.FromConfig(new McpServerConfig
        {
            Name = "local-files",
            Description = "Configured locally",
            Transport = "http",
            Endpoint = "https://token:secret@mcp.example.test:8443/tools?api_key=leak"
        });

        Assert.Equal("local-files", metadata["mcp.config.name"]);
        Assert.Equal("Configured locally", metadata["mcp.config.description"]);
        Assert.Equal("http", metadata["mcp.config.transport"]);
        Assert.Equal("https://mcp.example.test:8443", metadata["mcp.config.endpoint"]);
        Assert.DoesNotContain(metadata.Keys, key => key.StartsWith("mcp.client.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not an endpoint")]
    [InlineData("/relative/path")]
    public void NormalizeEndpoint_ReturnsNull_WhenEndpointIsNotAnAbsoluteNetworkUri(string? endpoint)
    {
        Assert.Null(McpTelemetryMetadata.NormalizeEndpoint(endpoint));
    }

    [Fact]
    public void FromConfig_OmitsEndpoint_WhenNoSafeEndpointIsConfigured()
    {
        var metadata = McpTelemetryMetadata.FromConfig(new McpServerConfig
        {
            Name = "local-process",
            Transport = "stdio"
        });

        Assert.DoesNotContain("mcp.config.endpoint", metadata.Keys);
    }

    [Fact]
    public void ForToolCall_PreservesRemoteIdentity_AndExcludesToolArguments()
    {
        var metadata = McpTelemetryMetadata.ForToolCall(
            new McpServerConfig { Name = "configured", Transport = "stdio" },
            "read_file",
            new Dictionary<string, object?>
            {
                ["mcp.server.name"] = "remote-server",
                ["mcp.server.version"] = "1.2.3"
            });

        Assert.Equal("remote-server", metadata["mcp.server.name"]);
        Assert.Equal("1.2.3", metadata["mcp.server.version"]);
        Assert.Equal("configured", metadata["mcp.config.name"]);
        Assert.Equal("read_file", metadata["mcp.method.name"]);
        Assert.DoesNotContain("mcp.method.arguments", metadata.Keys);
    }
}
