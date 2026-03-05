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

[KernelPlugin(Description = "Model Context Protocol（MCP）客户端插件。提供 MCP 服务器管理、工具列举和调用、资源和提示词列表等功能。", Version = "1.3")]
public class UseMCPPlugin : BasePlugin
{
    private readonly ILogger<UseMCPPlugin> _logger;
    private readonly IRepository<MCPServer> _mcpServiceRepository;
    private readonly McpConnectionFactory? _cacheableMcpClientFactory;
    private readonly PromptTemplateService _promptTemplateService;

    public UseMCPPlugin(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _mcpServiceRepository = _serviceProvider.GetService<IRepository<MCPServer>>();
        _logger = _serviceProvider.GetService<ILoggerFactory>().CreateLogger<UseMCPPlugin>();
        _cacheableMcpClientFactory = _serviceProvider.GetService<McpConnectionFactory>();
        _promptTemplateService = _serviceProvider.GetService<PromptTemplateService>();
    }

    private McpConnectionFactory GetRequiredFactory()
    {
        return _cacheableMcpClientFactory
            ?? throw new InvalidOperationException("CacheableMcpClientFactory is not registered. MCP functionality requires CacheableMcpClientFactory to be registered in DI.");
    }

    [KernelFunction]
    [Description("列出当前应用已配置并启用的所有 MCP 服务器名称和简介")]
    public async Task<string> ListServersAsync(Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        var appId = agentExecutionContext.GetAppId();

        var mcpServers = await _mcpServiceRepository.FindListAsync(x => x.AppId == appId && x.Enabled == true);
        return JsonConvert.SerializeObject(mcpServers.Select(x => new { serverName = x.Name, description = x.Intro }));
    }

    [KernelFunction]
    [Description("列出指定 MCP 服务器支持的所有工具（Function），包含工具名称、描述和输入参数Schema")]
    public async Task<string> ListToolsAsync([Description("MCP 服务器名称")][Required] string serverName, Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        var appId = agentExecutionContext.GetAppId();

        var mcpServer = await _mcpServiceRepository.FindAsync(x => x.AppId == appId && x.Name == serverName);
        if (mcpServer == null) return $"Unable to find the MCP Server '{serverName}'";

        var tools = await GetRequiredFactory()
            .GetToolsAsync(mcpServer, forceRefresh: false);

        return FormatTools(serverName, tools);
    }

    [KernelFunction]
    [Description("强制刷新指定 MCP 服务器的工具列表缓存，确保获取最新的工具定义")]
    public async Task<string> RefreshToolsAsync([Description("MCP 服务器名称")][Required] string serverName, Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        var appId = agentExecutionContext.GetAppId();

        var mcpServer = await _mcpServiceRepository.FindAsync(x => x.AppId == appId && x.Name == serverName);
        if (mcpServer == null) return $"Unable to find the MCP Server '{serverName}'";

        await GetRequiredFactory().RefreshToolCacheAsync(mcpServer);
        var tools = await GetRequiredFactory()
            .GetToolsAsync(mcpServer, forceRefresh: true);

        return FormatTools(serverName, tools);
    }

    [KernelFunction]
    [Description("列出指定 MCP 服务器提供的可访问资源（Resources），如文件、数据等")]
    public async Task<string> ListResourcesAsync([Description("MCP 服务器名称")][Required] string serverName, Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        var appId = agentExecutionContext.GetAppId();

        var mcpServer = await _mcpServiceRepository.FindAsync(x => x.AppId == appId && x.Name == serverName);
        if (mcpServer == null) return $"Unable to find the MCP Server '{serverName}'";

        var resources = await GetRequiredFactory().GetOrCreate(mcpServer).GetResourcesAsync();

        return JsonConvert.SerializeObject(resources);
    }

    [KernelFunction]
    [Description("列出指定 MCP 服务器提供的提示词模板（Prompts），可复用这些模板生成内容")]
    public async Task<string> ListPromptsAsync([Description("MCP 服务器名称")][Required] string serverName, Kernel kernel)
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
    [Description("调用指定 MCP 服务器中的指定工具，传入工具名称和参数字典，返回工具执行结果")]
    public async Task<string> CallToolAsync(
        [Description("MCP 服务器名称")][Required] string serverName,
        [Description("要调用的工具名称")][Required] string toolName,
        [Description("工具参数，键值对字典")][Required] Dictionary<string, object> arguments,
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
    [Description("获取 MCP 连接池的统计信息，包括当前连接数和工具缓存数")]
    public string GetConnectionStats()
    {
        var stats = GetRequiredFactory().GetStats();
        return $"Connections: {stats.ConnectionCount}, ToolCache: {stats.ToolCacheCount}";
    }

    [KernelFunction]
    [Description("断开与指定 MCP 服务器的连接并清理相关资源")]
    public async Task<string> DisconnectAsync([Description("MCP 服务器名称")][Required] string serverName, Kernel kernel)
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
