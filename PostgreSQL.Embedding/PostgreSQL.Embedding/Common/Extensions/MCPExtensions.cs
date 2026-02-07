using Masuit.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using PostgreSQL.Embedding.Domain.Entities;
using System.Text.Json;
using System.Text.Json.Serialization;
using PostgreSQL.Embedding.Infrastructure.DataAccess;

namespace PostgreSQL.Embedding.Common.Extensions
{
    public static class MCPExtensions
    {
        public static async Task<IEnumerable<KernelFunction>> GetKernelFunctionsAsync(this McpClient client, ILoggerFactory loggerFactory, IServiceProvider serviceProvider, bool cacheToolsList = true)
        {
            var mcpToolResository = serviceProvider.GetRequiredService<IRepository<MCPTool>>();

            if (!cacheToolsList)
            {
                var toolsList = await client.ListToolsAsync().ConfigureAwait(false);
                return toolsList.Select(tool => ToKernelFunction(new McpClientToolWrapper(tool), client, loggerFactory)).ToList();
            }

            var serverName = client.ServerInfo.Name;
            var serverVersion = client.ServerInfo.Version;
            var cachedMcpTools = await mcpToolResository.FindListAsync(x => x.ServerName == serverName && x.ServerVersion == serverVersion);
            if (cachedMcpTools.Any())
            {
                var toolsList = cachedMcpTools.Select(x => new McpClientToolWrapper(x)).ToList();
                return toolsList.Select(tool => ToKernelFunction(tool, client, loggerFactory)).ToList();
            }

            var tools = await client.ListToolsAsync().ConfigureAwait(false);
            await PersistMcpClientTools(serverName, serverVersion, tools, mcpToolResository);
            return tools.Select(tool => ToKernelFunction(new McpClientToolWrapper(tool), client, loggerFactory)).ToList();
        }

        private static KernelFunction ToKernelFunction(this McpClientToolWrapper tool, McpClient client, ILoggerFactory loggerFactory)
        {
            async Task<string> InvokeToolAsync(Kernel kernel, KernelFunction function, KernelArguments arguments, CancellationToken cancellationToken)
            {
                var logger = loggerFactory.CreateLogger<KernelFunction>();

                try
                {
                    var mcpArguments = new Dictionary<string, object>();
                    foreach (var arg in arguments)
                    {
                        if (arg.Value is not null) mcpArguments[arg.Key] = function.ToArgumentValue(arg.Key, arg.Value);
                    }

                    var result = await client.CallToolAsync(tool.Name, mcpArguments, cancellationToken: cancellationToken).ConfigureAwait(false);

                    return string.Join("\n", result.Content.Where(c => c.Type == "text").Select(c => c.ToString()));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"An error occurs when invoking tool '{tool.Name}'");
                    return ex.Message;
                }
            }

            return KernelFunctionFactory.CreateFromMethod(
                method: InvokeToolAsync,
                functionName: tool.Name.Replace("-", "_"),
                description: tool.Description,
                parameters: ToKernelParameters(tool),
                returnParameter: ToKernelReturnParameter(),
                loggerFactory: loggerFactory ?? NullLoggerFactory.Instance
            );
        }

        private static List<KernelParameterMetadata> ToKernelParameters(McpClientToolWrapper tool)
        {
            var inputSchema = tool.InputSchema;
            var properties = inputSchema?.Properties;
            if (properties == null) return [];

            var requiredProperties = new HashSet<string>(inputSchema!.Required ?? []);
            return properties.Select(kvp => new KernelParameterMetadata(kvp.Key)
            {
                Description = kvp.Value.Description,
                ParameterType = ConvertParameterDataType(kvp.Value, requiredProperties.Contains(kvp.Key)),
                IsRequired = requiredProperties.Contains(kvp.Key)
            })
            .ToList();
        }

        private static Type ConvertParameterDataType(JsonSchemaProperty property, bool required)
        {
            var type = property.Type switch
            {
                "string" => typeof(string),
                "integer" => typeof(int),
                "number" => typeof(double),
                "boolean" => typeof(bool),
                "array" => typeof(List<string>),
                "object" => typeof(Dictionary<string, object>),
                _ => typeof(object)
            };

            return !required && type.IsValueType ? typeof(Nullable<>).MakeGenericType(type) : type;
        }

        private static KernelReturnParameterMetadata? ToKernelReturnParameter()
        {
            return new KernelReturnParameterMetadata()
            {
                ParameterType = typeof(string),
            };
        }

        private static object ToArgumentValue(this KernelFunction function, string name, object value)
        {
            if (value.GetType() == typeof(JsonElement))
                value = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>((JsonElement)value);

            var parameter = function.Metadata.Parameters.FirstOrDefault(p => p.Name == name);
            return parameter?.ParameterType switch
            {
                Type t when Nullable.GetUnderlyingType(t) == typeof(decimal) => Convert.ToDecimal(value),
                Type t when Nullable.GetUnderlyingType(t) == typeof(int) => Convert.ToInt32(value),
                Type t when Nullable.GetUnderlyingType(t) == typeof(double) => Convert.ToDouble(value),
                Type t when Nullable.GetUnderlyingType(t) == typeof(bool) => Convert.ToBoolean(value),
                Type t when t == typeof(List<string>) => (value as IEnumerable<object>)?.ToList(),
                Type t when t == typeof(Dictionary<string, object>) => (value as Dictionary<string, object>)?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                _ => value,
            } ?? value;
        }

        private static async Task PersistMcpClientTools(string serverName, string serverVersion, IList<McpClientTool> tools, IRepository<MCPTool> mcpToolResository)
        {
            await mcpToolResository.DeleteAsync(x => x.ServerName == serverName);

            var mcpTools = tools.Select(x => new MCPTool()
            {
                ServerName = serverName,
                ServerVersion = serverVersion,
                ToolName = x.Name,
                ToolDescription = x.Description,
                ToolInputSchema = JsonSerializer.Serialize(x.JsonSchema)
            });

            await mcpToolResository.AddAsync(mcpTools.ToArray());
        }
    }

    internal class JsonSchema
    {
        /// <summary>
        /// The type of the schema, should be "object".
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "object";

        /// <summary>
        /// Map of property names to property definitions.
        /// </summary>
        [JsonPropertyName("properties")]
        public Dictionary<string, JsonSchemaProperty>? Properties { get; set; }

        /// <summary>
        /// List of required property names.
        /// </summary>
        [JsonPropertyName("required")]
        public List<string>? Required { get; set; }
    }

    internal class JsonSchemaProperty
    {
        /// <summary>
        /// The type of the property. Should be a JSON Schema type and is required.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// A human-readable description of the property.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; } = string.Empty;
    }

    internal class McpClientToolWrapper
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public JsonSchema InputSchema { get; set; }

        public McpClientToolWrapper(McpClientTool tool)
        {
            Name = tool.Name;
            Description = tool.Description;
            InputSchema = tool.JsonSchema.Deserialize<JsonSchema>();
        }

        public McpClientToolWrapper(MCPTool tool)
        {
            Name = tool.ToolName;
            Description = tool.ToolDescription;

            var jsonElement = JsonDocument.Parse(tool.ToolInputSchema).RootElement;
            InputSchema = jsonElement.Deserialize<JsonSchema>();
        }
    }
}
