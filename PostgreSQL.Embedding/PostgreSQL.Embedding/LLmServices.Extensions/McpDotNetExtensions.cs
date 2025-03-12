using McpDotNet.Client;
using McpDotNet.Protocol.Types;
using Microsoft.SemanticKernel;

namespace PostgreSQL.Embedding.LLmServices.Extensions
{
    public static class McpDotNetExtensions
    {
        public static async Task<IEnumerable<KernelFunction>> GetKernelFunctionsAsync(this IMcpClient client)
        {
            var listToolsResult = await client.ListToolsAsync().ConfigureAwait(false);
            return listToolsResult.Tools.Select(tool => ToKernelFunction(tool, client)).ToList();
        }

        private static KernelFunction ToKernelFunction(this Tool tool, IMcpClient client)
        {
            async Task<string> InvokeToolAsync(Kernel kernel, KernelFunction function, KernelArguments arguments, CancellationToken cancellationToken)
            {
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
                catch
                {
                    throw;
                }
            }

            return KernelFunctionFactory.CreateFromMethod(
                method: InvokeToolAsync,
                functionName: tool.Name,
                description: tool.Description,
                parameters: ToKernelParameters(tool),
                returnParameter: ToKernelReturnParameter()
            );
        }

        private static List<KernelParameterMetadata> ToKernelParameters(Tool tool)
        {
            var inputSchema = tool.InputSchema;
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
}
