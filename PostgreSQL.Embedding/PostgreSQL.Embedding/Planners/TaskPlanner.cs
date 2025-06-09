using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.Common.Models.Planners;
using PostgreSQL.Embedding.LlmServices;
using PostgreSQL.Embedding.LLmServices.Extensions;
using PostgreSQL.Embedding.Plugins;
using System.Linq.Expressions;
using System.Text;

namespace PostgreSQL.Embedding.Planners
{
    public class TaskPlanner
    {
        private readonly Kernel _kernel;
        private readonly ILogger<TaskPlanner> _logger;
        private readonly CallablePromptTemplate _promptTemplate;
        private readonly PromptTemplateService _promptTemplateService = new PromptTemplateService();
        public TaskPlanner(Kernel kernel)
        {
            _kernel = kernel;
            _logger = _kernel.LoggerFactory.CreateLogger<TaskPlanner>();
            _promptTemplate = _promptTemplateService.LoadTemplate("TaskPlanner.txt");
        }

        public async Task<List<SubTask>> GetSubTasksAsync(string query, string history = null, int limit = 5)
        {
            _promptTemplate.AddVariable("input", query);
            _promptTemplate.AddVariable("language", "chinese");
            _promptTemplate.AddVariable("limit", limit);
            _promptTemplate.AddVariable("history", history);

            var functions = await CreateFunctionDescriptions(_kernel);
            _promptTemplate.AddVariable("functions", functions);

            var functionResult = string.Empty;
            await foreach (var content in _promptTemplate.InvokeStreamingAsync(_kernel))
            {
                functionResult += content.Content;
            }

            try
            {
                functionResult = functionResult.Replace("```json", "").Replace("```", "");
                functionResult = PreprocessJsonData(functionResult);
                var planResult = JsonConvert.DeserializeObject<PlanResult>(functionResult);
                return planResult.Tasks;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unable to create tasks for query '{query}'");
                return [];
            }
        }

        public async Task<List<SubTask>> GetRAGTasks(string query, string history = null)
        {
            _promptTemplate.AddVariable("input", query);
            _promptTemplate.AddVariable("language", "chinese");
            _promptTemplate.AddVariable("limit", 1);
            _promptTemplate.AddVariable("history", history);

            var functions = await CreateFunctionDescriptions(_kernel, x => x.PluginName == nameof(RAGFlowPlugin));
            _promptTemplate.AddVariable("functions", functions);

            var functionResult = string.Empty;
            await foreach (var content in _promptTemplate.InvokeStreamingAsync(_kernel))
            {
                functionResult += content.Content;
            }

            try
            {
                functionResult = functionResult.Replace("```json", "").Replace("```", "");
                functionResult = PreprocessJsonData(functionResult);
                var planResult = JsonConvert.DeserializeObject<PlanResult>(functionResult);
                return planResult.Tasks;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unable to create tasks for query '{query}'");
                return [];
            }
        }

        private Task<string> CreateFunctionDescriptions(Kernel kernel, Expression<Func<KernelFunctionMetadata, bool>> expression = null)
        {
            var availableFunctions = kernel.GetAvailableFunctions(expression);
            var functionDescriptions = string.Join("\r\n", availableFunctions.Select(x => CreateFunctionDescription(x)));

            var arguments = new KernelArguments() { ["functionDescriptions"] = functionDescriptions };
            return _promptTemplateService.RenderTemplateAsync("FunctionManual.txt", kernel, arguments);
        }

        private string CreateFunctionDescription(KernelFunctionMetadata functionMetadata, bool includeParameters = false)
        {
            var stringBuilder = new StringBuilder();
            var fullyQualifiedFunctionName = functionMetadata.GetFullyQualifiedFunctionName();
            stringBuilder.AppendLine($"{fullyQualifiedFunctionName}: {functionMetadata.Description.Trim()}");

            if (includeParameters)
            {
                foreach (var parameter in functionMetadata.Parameters)
                {
                    var defaultValueString = parameter.DefaultValue == null ? string.Empty : $"(default='{parameter.DefaultValue}')";
                    var parameterTypeString = $"(type='{parameter.ParameterType.Name}')";
                    stringBuilder.AppendLine($"  - {parameter.Name}: {parameter.Description.Trim()} {parameterTypeString} {defaultValueString}");
                }
            }

            return stringBuilder.ToString();
        }

        private string PreprocessJsonData(string jsonText)
        {
            var jsonObj = JObject.Parse(jsonText);
            var tasks = (JArray)jsonObj["tasks"];

            foreach (JObject task in tasks)
            {
                if (task.ContainsKey("execute_result"))
                {
                    var executeResult = task["execute_result"];

                    if (executeResult.Type == JTokenType.Object)
                    {
                        string serialized = executeResult.ToString(Formatting.Indented);
                        task["execute_result"] = serialized;
                    }
                    else
                    {
                        task["execute_result"] = executeResult.ToString();
                    }
                }
            }

            return jsonObj.ToString(Formatting.Indented);
        }
    }
}
