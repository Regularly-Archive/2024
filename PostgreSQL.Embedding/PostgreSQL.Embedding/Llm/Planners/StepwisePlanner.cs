using Masuit.Tools;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Llm.Services;
using System.Text;

namespace PostgreSQL.Embedding.Llm.Planners
{
    public class StepwisePlanner : IStepwisePlanner
    {
        private readonly Kernel _kernel;
        private readonly StepwisePlannerConfig _config;
        private readonly PromptTemplateService _promptTemplateService;

        public StepwisePlanner(Kernel kernel, PromptTemplateService promptTemplateService, StepwisePlannerConfig? config = null)
        {
            _kernel = kernel;
            _config = config ?? new StepwisePlannerConfig();
            _promptTemplateService = promptTemplateService;
        }

        public async Task<StepwisePlan> CreatePlanAsync()
        {
            var functionDescriptions = await CreateFunctionDescriptions(_kernel);
            var variableDescriptions = CreateVariableDescriptions();

            var arguments = new KernelArguments()
            {
                ["functionDescriptions"] = functionDescriptions,
                ["variableDescriptions"] = variableDescriptions,
                ["suffix"] = _config.Suffix
            };
            var systemMessage = await _promptTemplateService.RenderTemplateAsync("Stepwise.txt", _kernel, arguments);

            var logger = _kernel.LoggerFactory.CreateLogger<StepwisePlan>();

            return new StepwisePlan(systemMessage, _config, logger, _kernel);
        }

        public async Task<StepwisePlan> CreatePlanAsync(string instruction, List<string> functions)
        {
            var kernelFunctions = functions.Select(x => _kernel.GetKernelFunction(x)).ToList();

            var functionDescriptions = await CreateFunctionDescriptions(_kernel, kernelFunctions);
            var variableDescriptions = CreateVariableDescriptions();

            var arguments = new KernelArguments()
            {
                ["functionDescriptions"] = functionDescriptions,
                ["variableDescriptions"] = variableDescriptions,
                ["suffix"] = _config.Suffix
            };
            var systemMessage = await _promptTemplateService.RenderTemplateAsync("Stepwise.txt", _kernel, arguments);

            var logger = _kernel.LoggerFactory.CreateLogger<StepwisePlan>();

            var config = new StepwisePlannerConfig();
            config.Suffix = instruction ?? string.Empty;
            return new StepwisePlan(systemMessage, config, logger, _kernel);
        }

        public void AddVariable<T>(string key, T value)  => _config.Variables.Add(key, value);

        private Task<string> CreateFunctionDescriptions(Kernel kernel)
        {
            var availableFunctions = kernel.GetAvailableFunctions(x => !_config.ExcludedPlugins.Contains(x.PluginName) && !_config.ExcludedFunctions.Contains(x.GetFullyQualifiedFunctionName()));
            var functionDescriptions = string.Join("\r\n", availableFunctions.Select(x => CreateFunctionDescription(x)));

            var arguments = new KernelArguments() { ["functionDescriptions"] = functionDescriptions };
            return _promptTemplateService.RenderTemplateAsync("FunctionManual.txt", kernel, arguments);
        }

        private Task<string> CreateFunctionDescriptions(Kernel kernel, IList<KernelFunction> kernelFunctions)
        {
            var availableFunctions = kernelFunctions.Select(x => x.Metadata).ToList();
            var functionDescriptions = string.Join("\r\n", availableFunctions.Select(x => CreateFunctionDescription(x)));

            var arguments = new KernelArguments() { ["functionDescriptions"] = functionDescriptions };
            return _promptTemplateService.RenderTemplateAsync("FunctionManual.txt", kernel, arguments);
        }

        private string CreateVariableDescriptions()
        {
            var stringBuilder = new StringBuilder();
            foreach(var variable in  _config.Variables)
            {
                stringBuilder.AppendLine($"{variable.Key}: {variable.Value.ToString()}");
            }

            return stringBuilder.ToString(); ;
        }

        private string CreateFunctionDescription(KernelFunctionMetadata functionMetadata)
        {
            var stringBuilder = new StringBuilder();
            var fullyQualifiedFunctionName = functionMetadata.GetFullyQualifiedFunctionName();
            stringBuilder.AppendLine($"{fullyQualifiedFunctionName}: {functionMetadata.Description.Trim()}");
            foreach (var parameter in functionMetadata.Parameters)
            {
                var defaultValueString = parameter.DefaultValue == null ? string.Empty : $"(default='{parameter.DefaultValue}')";
                var parameterTypeString = $"(type='{parameter.ParameterType.Name}')";
                stringBuilder.AppendLine($"  - {parameter.Name}: {parameter.Description.Trim()} {parameterTypeString} {defaultValueString}");
            }

            return stringBuilder.ToString();
        }
    }
}
