using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Llm.Services;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;

namespace PostgreSQL.Embedding.Plugins.BuiltIn;

[KernelPlugin(Description = "一个帮助大模型更好地使用 MCP 协议的插件，提供服务器选择、工具列举、工具调用三个能力", Version = "1.2")]
public class UseMCPPlugin : BasePlugin
{
    private readonly ILogger<UseMCPPlugin> _logger;
    private readonly IRepository<MCPServer> _mcpServiceRepository;
    private readonly CacheableMcpClientFactory? _cacheableMcpClientFactory;
    private readonly PromptTemplateService _promptTemplateService;

    // 移除实例字段，改为每次从 Factory 获取
    // 原因：1. 多线程环境下状态污染
    //       2. 同一个插件实例可能被多个会话共享
    //       3. 使用 Factory 管理连接更安全

    public UseMCPPlugin(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _mcpServiceRepository = _serviceProvider.GetService<IRepository<MCPServer>>();
        _logger = _serviceProvider.GetService<ILoggerFactory>().CreateLogger<UseMCPPlugin>();
        _cacheableMcpClientFactory = _serviceProvider.GetService<CacheableMcpClientFactory>();
        _promptTemplateService = _serviceProvider.GetService<PromptTemplateService>();
    }

    private CacheableMcpClientFactory GetRequiredFactory()
    {
        return _cacheableMcpClientFactory
            ?? throw new InvalidOperationException("CacheableMcpClientFactory is not registered. MCP functionality requires CacheableMcpClientFactory to be registered in DI.");
    }

    [KernelFunction]
    [Description("列举当前应用可用的 MCP 服务器")]
    public async Task<string> ListServersAsync(Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        var appId = agentExecutionContext.GetAppId();

        var mcpServers = await _mcpServiceRepository.FindListAsync(x => x.AppId == appId && x.Enabled == true);
        return JsonConvert.SerializeObject(mcpServers.Select(x => new { serverName = x.Name, description = x.Intro }));
    }

    [KernelFunction]
    [Description("列举指定 MCP 服务器中支持的工具, 参数示例: {\"serverName\":\"腾讯EdgeOne\"}")]
    public async Task<string> ListToolsAsync([Description("服务器名称")][Required] string serverName, Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        var appId = agentExecutionContext.GetAppId();

        var mcpServer = await _mcpServiceRepository.FindAsync(x => x.AppId == appId && x.Name == serverName);
        if (mcpServer == null) return $"Unable to find the MCP Server '{serverName}'";

        // 使用 Factory 获取工具（带缓存）
        var tools = await GetRequiredFactory().GetToolsAsync(mcpServer, forceRefresh: false);

        return FormatTools(serverName, tools);
    }

    [KernelFunction]
    [Description("强制刷新指定 MCP 服务器的工具列表缓存, 参数示例: {\"serverName\":\"腾讯EdgeOne\"}")]
    public async Task<string> RefreshToolsAsync([Description("服务器名称")][Required] string serverName, Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        var appId = agentExecutionContext.GetAppId();

        var mcpServer = await _mcpServiceRepository.FindAsync(x => x.AppId == appId && x.Name == serverName);
        if (mcpServer == null) return $"Unable to find the MCP Server '{serverName}'";

        // 强制刷新缓存
        await GetRequiredFactory().RefreshToolCacheAsync(mcpServer);
        var tools = await GetRequiredFactory().GetToolsAsync(mcpServer, forceRefresh: true);

        return $"# Refreshed tools for {serverName}\n\n" + FormatTools(serverName, tools);
    }

    [KernelFunction]
    [Description("列举指定 MCP 服务器中支持的资源, 参数示例: {\"serverName\":\"腾讯EdgeOne\"}")]
    public async Task<string> ListResourcesAsync([Description("服务器名称")][Required] string serverName, Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        var appId = agentExecutionContext.GetAppId();

        var mcpServer = await _mcpServiceRepository.FindAsync(x => x.AppId == appId && x.Name == serverName);
        if (mcpServer == null) return $"Unable to find the MCP Server '{serverName}'";

        // 从连接直接获取资源
        var connection = GetRequiredFactory().GetOrCreate(mcpServer);
        var resources = await connection.Client.ListResourcesAsync();

        return JsonConvert.SerializeObject(resources);
    }

    [KernelFunction]
    [Description("列举指定 MCP 服务器中支持的提示词, 参数示例: {\"serverName\":\"腾讯EdgeOne\"}")]
    public async Task<string> ListPromptsAsync([Description("服务器名称")][Required] string serverName, Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        var appId = agentExecutionContext.GetAppId();

        var mcpServer = await _mcpServiceRepository.FindAsync(x => x.AppId == appId && x.Name == serverName);
        if (mcpServer == null) return $"Unable to find the MCP Server '{serverName}'";

        // 从连接直接获取提示词
        var connection = GetRequiredFactory().GetOrCreate(mcpServer);
        var prompts = await connection.Client.ListPromptsAsync();

        return JsonConvert.SerializeObject(prompts);
    }

    [KernelFunction]
    [Description("调用指定 MCP 服务器中的指定工具, 工具参数请封装到 arguments 字段中，参数示例: {\"serverName\":\"腾讯EdgeOne\",\"toolName\":\"deploy_html\",\"arguments\":{\"parameter1\":\"a\",\"parameter2\":10}}")]
    public async Task<string> CallToolAsync(
        [Description("服务器名称")][Required] string serverName,
        [Description("工具名称")][Required] string toolName,
        [Description("工具参数")][Required] Dictionary<string, object> arguments,
        Kernel kernel
    )
    {
        try
        {
            var agentExecutionContext = kernel.GetAgentExecutionContext();
            var appId = agentExecutionContext.GetAppId();

            var mcpServer = await _mcpServiceRepository.FindAsync(x => x.AppId == appId && x.Name == serverName);
            if (mcpServer == null) return $"Unable to find the MCP Server '{serverName}'";

            // 使用 Factory 调用工具（带重试和超时）
            var result = await GetRequiredFactory().CallToolAsync(mcpServer, toolName, arguments);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurs when invoking MCP tool '{ToolName}' on server '{ServerName}'", toolName, serverName);
            return $"Error: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("获取 MCP 连接状态统计信息")]
    public string GetConnectionStats()
    {
        var stats = GetRequiredFactory().GetStats();
        return $"Connections: {stats.ConnectionCount}, ToolCache: {stats.ToolCacheCount}";
    }

    [KernelFunction]
    [Description("断开并清理指定 MCP 服务器的连接")]
    public async Task<string> DisconnectAsync([Description("服务器名称")][Required] string serverName, Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        var appId = agentExecutionContext.GetAppId();

        var mcpServer = await _mcpServiceRepository.FindAsync(x => x.AppId == appId && x.Name == serverName);
        if (mcpServer == null) return $"Unable to find the MCP Server '{serverName}'";

        GetRequiredFactory().Remove(mcpServer);
        return $"Disconnected from MCP server '{serverName}'";
    }

    private string FormatTools(string serverName, IList<McpClientTool> tools)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# The available tools of {serverName}");
        sb.AppendLine();

        foreach (var tool in tools)
        {
            sb.AppendLine($"## {tool.Name}");
            sb.AppendLine($"- Description: {tool.Description}");
            sb.AppendLine("- InputSchema: ");
            sb.AppendLine("```json");
            sb.AppendLine(System.Text.Json.JsonSerializer.Serialize(tool.JsonSchema, new JsonSerializerOptions { WriteIndented = true }));
            sb.AppendLine("```");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
