
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.LlmServices;
using PostgreSQL.Embedding.LLmServices.Extensions;
using System.Text;

namespace PostgreSQL.Embedding.Planners
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

        public async Task<StepwisePlan> CreatePlanAsync(string goal)
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

            return new StepwisePlan(systemMessage, goal, _config, logger);
        }

        public void AddVariable<T>(string key, T value)  => _config.Variables.Add(key, value);

        private Task<string> CreateFunctionDescriptions(Kernel kernel)
        {
            var availableFunctions = kernel.GetAvailableFunctions(x => !_config.ExcludedPlugins.Contains(x.PluginName) && !_config.ExcludedFunctions.Contains(x.GetFullyQualifiedFunctionName()));
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
