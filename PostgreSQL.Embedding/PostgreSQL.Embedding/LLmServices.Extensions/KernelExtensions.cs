using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.SemanticKernel;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace PostgreSQL.Embedding.LLmServices.Extensions
{
    public static class KernelExtensions
    {
        public static IList<KernelFunctionMetadata> GetAvailableFunctions(this Kernel kernel, Expression<Func<KernelFunctionMetadata, bool>> expression = null)
        {
            if (expression == null) expression = f => true;
            var predicate = expression.Compile();
            return kernel.Plugins.GetFunctionsMetadata().Where(predicate).ToList();
        }

        public static string GetFullyQualifiedFunctionName(this KernelFunctionMetadata functionMetadata)
        {
            return $"{functionMetadata.PluginName}.{functionMetadata.Name}";
        }

        public static KernelFunction GetKernelFunction(this Kernel kernel, string fullyQualifiedFunctionName)
        {
            var splitedNames = fullyQualifiedFunctionName.Split(new char[] { '.' });

            var pluginName = splitedNames[0];
            var functionName = splitedNames[1];

            return kernel.Plugins.GetFunction(pluginName, functionName);
        }

        public static KernelArguments MergeArguments(this KernelArguments kernelArguments, Dictionary<string, object> dictionary)
        {
            foreach (var kv in dictionary)
            {
                kernelArguments[kv.Key] = kv.Value;
            }

            return kernelArguments;
        }
    }
}
