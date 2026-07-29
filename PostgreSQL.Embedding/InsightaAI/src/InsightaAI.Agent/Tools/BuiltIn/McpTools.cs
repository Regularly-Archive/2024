using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Mcp;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// MCP 工具集 - 提供 list_mcp_tools, activate_mcp_tool, deactivate_mcp_tool
/// </summary>
public static class McpTools
{
    private static readonly TimeSpan ListToolsTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void RegisterAll(ToolRegistry registry, McpRegistry mcpRegistry)
    {
        RegisterListMcpTools(registry, mcpRegistry);
        RegisterActivateMcpTool(registry, mcpRegistry);
        RegisterDeactivateMcpTool(registry, mcpRegistry);
    }

    private static void RegisterListMcpTools(ToolRegistry registry, McpRegistry mcpRegistry)
    {
        var schema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""server_name"": {
                    ""type"": ""string"",
                    ""description"": ""MCP server name. If empty, lists all servers and their tools.""
                }
            },
            ""required"": []
        }");

        registry.RegisterFunction(
            "list_mcp_tools",
            "List MCP servers and their tools. If server_name is not provided, lists an overview of all servers; if provided, lists all tool details for that server.",
            schema,
            async (args, ctx) =>
            {
                var arguments = new ToolArgumentReader(schema, args);
                arguments.TryGetString("server_name", out var serverName);

                if (string.IsNullOrEmpty(serverName))
                {
                    // 列出所有服务器
                    var servers = await mcpRegistry.ListAllServersAsync(ctx.CancellationToken);
                    if (servers.Count == 0)
                    {
                        return ToolResult.FromText("No MCP servers configured.");
                    }

                    var result = new Dictionary<string, object>();
                    foreach (var server in servers)
                    {
                        try
                        {
                            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
                            timeout.CancelAfter(ListToolsTimeout);
                            var tools = await mcpRegistry.ListToolsAsync(server.Name, timeout.Token);
                            result[server.Name] = new
                            {
                                description = server.Description,
                                tools = tools.Select(t => new { name = t.Name, description = t.Description }).ToArray()
                            };
                        }
                        catch (OperationCanceledException) when (!ctx.CancellationToken.IsCancellationRequested)
                        {
                            result[server.Name] = new
                            {
                                description = server.Description,
                                error = $"Timed out while listing tools after {ListToolsTimeout.TotalSeconds:0} seconds."
                            };
                        }
                        catch (Exception ex)
                        {
                            result[server.Name] = new
                            {
                                description = server.Description,
                                error = $"Unable to list tools: {ex.Message}"
                            };
                        }
                    }

                    return ToolResult.FromText(JsonSerializer.Serialize(result, JsonOptions));
                }
                else
                {
                    // 列出指定服务器的工具
                    var tools = await mcpRegistry.ListToolsAsync(serverName, ctx.CancellationToken);
                    if (tools.Count == 0)
                    {
                        return ToolResult.FromText($"No tools found for server '{serverName}'.");
                    }

                    var toolList = tools.Select(t => new
                    {
                        name = t.Name,
                        registered_name = t.RegisteredName,
                        description = t.Description,
                        input_schema = t.InputSchema
                    }).ToArray();

                    return ToolResult.FromText(JsonSerializer.Serialize(toolList, JsonOptions));
                }
            });
    }

    private static void RegisterActivateMcpTool(ToolRegistry registry, McpRegistry mcpRegistry)
    {
        var schema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""server_name"": {
                    ""type"": ""string"",
                    ""description"": ""MCP server name""
                },
                ""tool_name"": {
                    ""type"": ""string"",
                    ""description"": ""The name of the tool to activate""
                }
            },
            ""required"": [""server_name"", ""tool_name""]
        }");

        registry.RegisterFunction(
            "activate_mcp_tool",
            "Activate an MCP tool so it can be called by the agent. After activation, the tool name becomes mcp__{server}__{tool}.",
            schema,
            async (args, ctx) =>
            {
                var arguments = new ToolArgumentReader(schema, args);
                var serverName = arguments.GetString("server_name");
                var toolName = arguments.GetString("tool_name");

                var metadata = await mcpRegistry.ActivateToolAsync(serverName, toolName, registry, ctx.CancellationToken);
                if (metadata == null)
                {
                    return ToolResult.FromError($"Tool '{toolName}' not found on server '{serverName}'.");
                }

                return ToolResult.FromText(JsonSerializer.Serialize(new
                {
                    status = "activated",
                    registered_name = metadata.RegisteredName,
                    description = metadata.Description
                }, JsonOptions));
            });
    }

    private static void RegisterDeactivateMcpTool(ToolRegistry registry, McpRegistry mcpRegistry)
    {
        var schema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""registered_name"": {
                    ""type"": ""string"",
                    ""description"": ""The registered name of the activated tool (format: mcp__{server}__{tool})""
                }
            },
            ""required"": [""registered_name""]
        }");

        registry.RegisterFunction(
            "deactivate_mcp_tool",
            "Deactivate an activated MCP tool.",
            schema,
            (args, ctx) =>
            {
                var arguments = new ToolArgumentReader(schema, args);
                var registeredName = arguments.GetString("registered_name");

                var removed = mcpRegistry.DeactivateTool(registeredName, registry);
                if (!removed)
                {
                    return Task.FromResult(ToolResult.FromError($"Tool '{registeredName}' is not active."));
                }

                return Task.FromResult(ToolResult.FromText(JsonSerializer.Serialize(new
                {
                    status = "deactivated",
                    registered_name = registeredName
                }, JsonOptions)));
            });
    }
}
