using DocumentFormat.OpenXml.Math;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Common.Utilities;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Domain.Models.Planners;
using PostgreSQL.Embedding.Llm.Services;
using PostgreSQL.Embedding.Plugins.BuiltIn;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;

namespace PostgreSQL.Embedding.Llm.Planners
{
    public class TaskPlanner
    {
        private readonly Kernel _kernel;
        private readonly ILogger<TaskPlanner> _logger;
        private readonly CallablePromptTemplate _promptTemplate;
        private readonly PromptTemplateService _promptTemplateService = new PromptTemplateService();
        private static readonly Regex _jsonBlockRegex = new Regex(@"```json\s*([\s\S]*?)\s*```", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public TaskPlanner(Kernel kernel)
        {
            _kernel = kernel;
            _logger = _kernel.LoggerFactory.CreateLogger<TaskPlanner>();
            _promptTemplate = _promptTemplateService.LoadTemplate("TaskPlanner.txt");
        }

        public async Task<PlanResult> GetSubTasksAsync(string query, string history = null, int limit = 5)
        {
            _promptTemplate.AddVariable("input", query);
            _promptTemplate.AddVariable("language", "Chinese");
            _promptTemplate.AddVariable("limit", limit);
            _promptTemplate.AddVariable("history", history);

            var functions = await CreateFunctionDescriptions(_kernel);
            _promptTemplate.AddVariable("functions", functions);
            _promptTemplate.PluginName = nameof(TaskPlanner);
            _promptTemplate.FunctionName = "GetSubTasks";

            var functionResult = string.Empty;
            await foreach (var content in _promptTemplate.InvokeStreamingAsync(_kernel))
            {
                functionResult += content.Content;
            }

            try
            {
                functionResult = ExtractJson(functionResult);
                //functionResult = JsonRepairer.Repair(functionResult);
                functionResult = PreprocessJsonData(functionResult);
                var planResult = JsonConvert.DeserializeObject<PlanResult>(functionResult);
                return planResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unable to create tasks for query '{query}'");
                return new PlanResult() { };
            }
        }

        public async Task<PlanResult> GetRAGTasks(string query, string history = null)
        {
            _promptTemplate.AddVariable("input", query);
            _promptTemplate.AddVariable("language", "Chinese");
            _promptTemplate.AddVariable("limit", 1);
            _promptTemplate.AddVariable("history", history);

            var functions = await CreateFunctionDescriptions(_kernel, x => x.PluginName == nameof(RAGFlowPlugin));
            _promptTemplate.AddVariable("functions", functions);
            _promptTemplate.PluginName = nameof(TaskPlanner);
            _promptTemplate.FunctionName = "GetRAGTasks";

            var functionResult = string.Empty;
            await foreach (var content in _promptTemplate.InvokeStreamingAsync(_kernel))
            {
                functionResult += content.Content;
            }

            try
            {
                if (string.IsNullOrEmpty(functionResult))
                    return await GetRAGTasks(query, history);

                functionResult = ExtractJson(functionResult);
                //functionResult = JsonRepairer.Repair(functionResult);
                functionResult = PreprocessJsonData(functionResult);
                var planResult = JsonConvert.DeserializeObject<PlanResult>(functionResult);
                return planResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unable to create tasks for query '{query}'");
                return new PlanResult() { };
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

        public static string ExtractJson(string text)
        {
            Match match = _jsonBlockRegex.Match(text);
            return match.Success ? match.Groups[1].Value.Trim() : text;
        }
    }
}
