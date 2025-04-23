using Masuit.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PostgreSQL.Embedding.LLmServices.Extensions
{
    public static class MCPExtensions
    {
        public static async Task<IEnumerable<KernelFunction>> GetKernelFunctionsAsync(this IMcpClient client, ILoggerFactory loggerFactory)
        {
            var listToolsResult = await client.ListToolsAsync().ConfigureAwait(false);
            return listToolsResult.Select(tool => ToKernelFunction(tool, client, loggerFactory)).ToList();
        }

        private static KernelFunction ToKernelFunction(this McpClientTool tool, IMcpClient client, ILoggerFactory loggerFactory)
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

                    var result = await client.CallToolAsync(
                        tool.Name,
                        mcpArguments,
                        cancellationToken: cancellationToken
                    ).ConfigureAwait(false);

                    return string.Join("\n", result.Content
                        .Where(c => c.Type == "text")
                        .Select(c => c.Text));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"An error occurs when invoking tool '{tool.Name}'");
                    return ex.Message;
                }
            }

            return KernelFunctionFactory.CreateFromMethod(
                method: InvokeToolAsync,
                functionName: tool.Name.Replace("-","_"),
                description: tool.Description,
                parameters: ToKernelParameters(tool),
                returnParameter: ToKernelReturnParameter(),
                loggerFactory: loggerFactory ?? NullLoggerFactory.Instance
            );
        }

        private static List<KernelParameterMetadata> ToKernelParameters(McpClientTool tool)
        {
            var inputSchema = tool.JsonSchema.Deserialize<JsonSchema>();
            var properties = inputSchema?.Properties;
            if (properties == null) return [];

            HashSet<string> requiredProperties = new(inputSchema!.Required ?? []);
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
            var parameter = function.Metadata.Parameters.FirstOrDefault(p => p.Name == name);
            return parameter?.ParameterType switch
            {
                Type t when Nullable.GetUnderlyingType(t) == typeof(int) => Convert.ToInt32(value),
                Type t when Nullable.GetUnderlyingType(t) == typeof(double) => Convert.ToDouble(value),
                Type t when Nullable.GetUnderlyingType(t) == typeof(bool) => Convert.ToBoolean(value),
                Type t when t == typeof(List<string>) => (value as IEnumerable<object>)?.ToList(),
                Type t when t == typeof(Dictionary<string, object>) => (value as Dictionary<string, object>)?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                _ => value,
            } ?? value;
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
}
