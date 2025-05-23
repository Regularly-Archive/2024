using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices;
using PostgreSQL.Embedding.LLmServices.Extensions;
using PostgreSQL.Embedding.Planners;
using PostgreSQL.Embedding.Plugins.Abstration;
using PostgreSQL.Embedding.Utils;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "一个帮助大模型更好地使用 MCP 协议的插件，提供服务器选择、工具列举、工具调用三个能力")]
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
        [Description("为当前应用筛选出最匹配用户需求的 MCP 服务器")]
        public async Task<string> ChooseMCPServerAsync(
            [Description("当前应用ID")][Required] long appId, 
            [Description("用户请求")][Required] string query, 
            Kernel kernel
        )
        {
            var mcpServers = await _mcpServiceRepository.FindListAsync(x => x.AppId == appId);

            var promptTemplate = _promptTemplateService.LoadTemplate("UseMcp.txt");
            promptTemplate.AddVariable("serverNames", string.Join("\r\n", mcpServers.Select(x => $"* {x.Name}: {x.Intro}").ToList()));
            promptTemplate.AddVariable("query", query);

            var functionResult = string.Empty;

            await foreach (var content in promptTemplate.InvokeStreamingAsync(kernel))
            {
                functionResult += content.Content;
            }

            functionResult = functionResult.Replace("```json", "").Replace("```", "").Trim();
            return functionResult;
        }

        [KernelFunction]
        [Description("列举指定 MCP 服务器中支持的工具")]
        public async Task<string> ListToolsAsync(
            [Description("当前应用ID")][Required] long appId,
            [Description("服务器名称")][Required] string serverName,
            Kernel kernel
        )
        {
            var mcpServer = await _mcpServiceRepository.FindAsync(x => x.AppId == appId && x.Name == serverName);
            if (mcpServer == null) return $"Unable to find the MCP Server '{serverName}'";

            await kernel.AddMCPServerAsync(mcpServer, _cacheableMcpClientFactory);

            _client = _cacheableMcpClientFactory.GetOrCreate(mcpServer);
            _tools = await _client.ListToolsAsync();

            var toolDescriptors = _tools.Select(x =>
            {
                _stringBuilder.Clear();
                _stringBuilder.AppendLine($"Name: {x.Name}");
                _stringBuilder.AppendLine($"Description: {x.Description}");
                _stringBuilder.AppendLine($"InputSchema: ");
                _stringBuilder.AppendLine("```json");
                _stringBuilder.AppendLine(System.Text.Json.JsonSerializer.Serialize(x.JsonSchema));
                _stringBuilder.AppendLine("```");
                return _stringBuilder.ToString();
            }).ToList();

            return string.Join("\r\n", toolDescriptors);
        }


        [KernelFunction]
        [Description("调用指定 MCP 服务器中的指定工具")]
        public async Task<string> CallToolAsync(
            [Description("当前应用ID")] long appId,
            [Description("服务器名称")] string serverName,
            [Description("工具名称")] string toolName,
            [Description("工具参数")] Dictionary<string, object> arguments
        )
        {
            try
            {
                var mcpServer = await _mcpServiceRepository.FindAsync(x => x.AppId == appId && x.Name == serverName);
                if (mcpServer == null) return $"Unable to find the MCP Server '{serverName}'";

                var mcpClient = _cacheableMcpClientFactory.GetOrCreate(mcpServer);
                var result = await mcpClient.CallToolAsync(toolName, arguments).ConfigureAwait(false);

                return string.Join("\n", result.Content
                .Where(c => c.Type == "text")
                    .Select(c => c.Text));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"An error occurs when invoking MCP tool '{toolName}'");
                return ex.Message;
            }
        }
    }
}
