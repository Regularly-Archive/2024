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

namespace PostgreSQL.Embedding.Plugins;

[KernelPlugin(Description = "一个帮助大模型更好地使用 MCP 协议的插件，提供服务器选择、工具列举、工具调用三个能力", Version = "1.2")]
public class UseMCPPlugin : BasePlugin
{
    private readonly ILogger<UseMCPPlugin> _logger;
    private readonly IRepository<MCPServer> _mcpServiceRepository;
    private readonly CacheableMcpClientFactory _cacheableMcpClientFactory;
    private readonly PromptTemplateService _promptTemplateService;

    private IMcpClient _client;
    private IList<McpClientTool> _tools;
    private StringBuilder _stringBuilder = new StringBuilder();

    public UseMCPPlugin(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _mcpServiceRepository = _serviceProvider.GetService<IRepository<MCPServer>>();
        _logger = _serviceProvider.GetService<ILoggerFactory>().CreateLogger<UseMCPPlugin>();
        _cacheableMcpClientFactory = new CacheableMcpClientFactory(_serviceProvider);
        _promptTemplateService = _serviceProvider.GetService<PromptTemplateService>();
    }

    [KernelFunction]
    [Description("列举当前应用可用的 MCP 服务器")]
    public async Task<string> ListServersAsync(Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        var appId = agentExecutionContext.GetAppId();

        var mcpServers = await _mcpServiceRepository.FindListAsync(x => x.AppId == appId);
        return System.Text.Json.JsonSerializer.Serialize(mcpServers.Select(x => new { serverName = x.Name, description = x.Intro }));
    }

    [KernelFunction]
    [Description("列举指定 MCP 服务器中支持的工具, 参数示例: {\"serverName\":\"腾讯EdgeOne\"}")]
    public async Task<string> ListToolsAsync([Description("服务器名称")][Required] string serverName, Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        var appId = agentExecutionContext.GetAppId();

        var mcpServer = await _mcpServiceRepository.FindAsync(x => x.AppId == appId && x.Name == serverName);
        if (mcpServer == null) return $"Unable to find the MCP Server '{serverName}'";

        _client = _cacheableMcpClientFactory.GetOrCreate(mcpServer);
        _tools = await _client.ListToolsAsync();

        var toolDescriptors = _tools.Select(x =>
        {
            _stringBuilder.Clear();
            _stringBuilder.AppendLine();
            _stringBuilder.AppendLine($"## {x.Name}");
            _stringBuilder.AppendLine($"* Description: {x.Description}");
            _stringBuilder.AppendLine($"* InputSchema: ");
            _stringBuilder.AppendLine("```json");
            _stringBuilder.AppendLine(System.Text.Json.JsonSerializer.Serialize(x.JsonSchema));
            _stringBuilder.AppendLine("```");
            return _stringBuilder.ToString();
        });

        return $"# The available tools of {serverName}\r\n" + string.Join($"\r\n", toolDescriptors.ToList());
    }

    [KernelFunction]
    [Description("列举指定 MCP 服务器中支持的资源, 参数示例: {\"serverName\":\"腾讯EdgeOne\"}")]
    public async Task<string> ListResourcesAsync([Description("服务器名称")][Required] string serverName, Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        var appId = agentExecutionContext.GetAppId();

        var mcpServer = await _mcpServiceRepository.FindAsync(x => x.AppId == appId && x.Name == serverName);
        if (mcpServer == null) return $"Unable to find the MCP Server '{serverName}'";

        var resources = await _client.ListResourcesAsync();
        return System.Text.Json.JsonSerializer.Serialize(resources);
    }

    [KernelFunction]
    [Description("列举指定 MCP 服务器中支持的提示词, 参数示例: {\"serverName\":\"腾讯EdgeOne\"}")]
    public async Task<string> ListPromptsAsync([Description("服务器名称")][Required] string serverName, Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        var appId = agentExecutionContext.GetAppId();

        var mcpServer = await _mcpServiceRepository.FindAsync(x => x.AppId == appId && x.Name == serverName);
        if (mcpServer == null) return $"Unable to find the MCP Server '{serverName}'";

        var prompts = await _client.ListPromptsAsync();
        return System.Text.Json.JsonSerializer.Serialize(prompts);
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

            var mcpClient = _cacheableMcpClientFactory.GetOrCreate(mcpServer);
            var result = await mcpClient.CallToolAsync(toolName, arguments).ConfigureAwait(false);

            return string.Join("\n", result.Content.Where(c => c.Type == "text").Select(c => c.Text));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"An error occurs when invoking MCP tool '{toolName}'");
            return ex.Message;
        }
    }
}
