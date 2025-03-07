using Elastic.Clients.Elasticsearch;
using MCPSharp;
using MCPSharp.Model;
using Microsoft.SemanticKernel;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PostgreSQL.Embedding.LLmServices.Extensions
{
    public static class MCPClientExtensions
    {
        public static async Task<IEnumerable<KernelFunction>> GetKernelFunctionsAsync(this MCPClient client)
        {
            var tools = await client.GetToolsAsync();
            return tools.Select(tool => ToKernelFunction(tool, client)).ToList();
        }

        private static KernelFunction ToKernelFunction(this Tool tool, MCPClient client)
        {
            async Task<string> InvokeToolAsync(Kernel kernel, KernelFunction function, KernelArguments arguments)
            {
                var toolCallResult = await client.CallToolAsync(function.Name, arguments.ToDictionary());
                return string.Join("\n", toolCallResult.Content.Select(c => c.Text));
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
            return tool.InputSchema.Properties.Select(x => new KernelParameterMetadata(x.Key)
            {
                Description = x.Value.Description,
                ParameterType = ConvertParameterDataType(x.Value.Type, x.Value.Required),
                IsRequired = x.Value.Required,
            })
            .ToList();
        }

        private static Type ConvertParameterDataType(string parameterType, bool required)
        {
            var type = parameterType switch
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
    }
}
