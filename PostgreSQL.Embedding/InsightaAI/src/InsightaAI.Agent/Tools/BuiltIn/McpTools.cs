using System.Text.Json;
using InsightaAI.Agent.Mcp;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// MCP 工具集 - 提供 list_mcp_tools, activate_mcp_tool, deactivate_mcp_tool
/// </summary>
public static class McpTools
{
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
                    ""description"": ""MCP 服务器名称。如果为空，则列出所有服务器及其工具。""
                }
            },
            ""required"": []
        }");

        registry.RegisterFunction(
            "list_mcp_tools",
            "列出 MCP 服务器及其工具。不传 server_name 则列出所有服务器概览；传入 server_name 则列出该服务器的所有工具详情。",
            schema,
            async (args, ctx) =>
            {
                var serverName = args.TryGetValue("server_name", out var val) ? val?.ToString() : null;

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
                        var tools = await mcpRegistry.ListToolsAsync(server.Name, ctx.CancellationToken);
                        result[server.Name] = new
                        {
                            description = server.Description,
                            tools = tools.Select(t => new { name = t.Name, description = t.Description }).ToArray()
                        };
                    }

                    return ToolResult.FromText(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
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

                    return ToolResult.FromText(JsonSerializer.Serialize(toolList, new JsonSerializerOptions { WriteIndented = true }));
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
                    ""description"": ""MCP 服务器名称""
                },
                ""tool_name"": {
                    ""type"": ""string"",
                    ""description"": ""要激活的工具名称""
                }
            },
            ""required"": [""server_name"", ""tool_name""]
        }");

        registry.RegisterFunction(
            "activate_mcp_tool",
            "激活一个 MCP 工具，使其可用于 Agent 调用。激活后工具名为 mcp__{server}__{tool}。",
            schema,
            async (args, ctx) =>
            {
                var serverName = args["server_name"]?.ToString();
                var toolName = args["tool_name"]?.ToString();

                if (string.IsNullOrEmpty(serverName))
                    return ToolResult.FromError("server_name is required");
                if (string.IsNullOrEmpty(toolName))
                    return ToolResult.FromError("tool_name is required");

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
                }, new JsonSerializerOptions { WriteIndented = true }));
            });
    }

    private static void RegisterDeactivateMcpTool(ToolRegistry registry, McpRegistry mcpRegistry)
    {
        var schema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""registered_name"": {
                    ""type"": ""string"",
                    ""description"": ""已激活工具的注册名称（格式：mcp__{server}__{tool}）""
                }
            },
            ""required"": [""registered_name""]
        }");

        registry.RegisterFunction(
            "deactivate_mcp_tool",
            "停用一个已激活的 MCP 工具。",
            schema,
            (args, ctx) =>
            {
                var registeredName = args["registered_name"]?.ToString();
                if (string.IsNullOrEmpty(registeredName))
                    return Task.FromResult(ToolResult.FromError("registered_name is required"));

                var removed = mcpRegistry.DeactivateTool(registeredName, registry);
                if (!removed)
                {
                    return Task.FromResult(ToolResult.FromError($"Tool '{registeredName}' is not active."));
                }

                return Task.FromResult(ToolResult.FromText(JsonSerializer.Serialize(new
                {
                    status = "deactivated",
                    registered_name = registeredName
                }, new JsonSerializerOptions { WriteIndented = true })));
            });
    }
}
