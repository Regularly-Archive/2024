using DocumentFormat.OpenXml.Office.SpreadSheetML.Y2023.MsForms;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Services;
using Microsoft.SemanticKernel.TextGeneration;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Common.Json;
using PostgreSQL.Embedding.Domain.Models.Planners;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace PostgreSQL.Embedding.Llm.Planners
{
    public class ReActAgent
    {
        private readonly string _systemMessage;
        private readonly StepwisePlannerConfig _config;
        private readonly ILogger<StepwisePlan> _logger;
        private readonly Kernel _kernel;
        private readonly AgentExecutionContext _agentExecutionContext;
        public string PlanId { get; private set; }

        private const string ObservationTag = "<Observation";
        private const string ThoughtTag = "[Thought]";
        private const string TrimMessageFormat = "... I've removed the first {0} steps of my previous work to make room for the new stuff ...";

        public Func<StepTrace, Task> OnStepExecute { get; set; }

        private Stopwatch _stopwatch;

        public ReActAgent(string systemMessage, StepwisePlannerConfig config, ILogger<StepwisePlan> logger, Kernel kernel)
        {
            _config = config;
            _systemMessage = systemMessage;
            _logger = logger;
            PlanId = Guid.NewGuid().ToString("N");
            _kernel = kernel;
            _agentExecutionContext = _kernel.GetAgentExecutionContext();
        }

        public async Task<string> ExecuteAsync(string goal, ChatHistory chatHistory = null, CancellationToken cancellationToken = default)
        {
            var stepsTaken = new List<ReasoningStep>();

            if (chatHistory == null) chatHistory = new ChatHistory();

            chatHistory.Insert(0, new ChatMessageContent(AuthorRole.System, _systemMessage, null, null, Encoding.UTF8, null));
            chatHistory.AddUserMessage($"<Question>{goal}</Question>");

            var aiService = GetAIService(_kernel);
            var startingMessageCount = chatHistory.Count;

            ReasoningStep? lastStep = null;

            for (var i = 0; i < _config.MaxIterations; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (i > 0) await Task.Delay(_config.MinIterationTimeSpan, cancellationToken).ConfigureAwait(false);

                var nextStep = await GetNextStepAsync(stepsTaken, chatHistory, aiService, startingMessageCount, cancellationToken);
                nextStep.Index = i;
                _logger.LogTrace($"Step {i + 1}: {nextStep.ToString()}");

                if (!string.IsNullOrEmpty(nextStep.Action))
                {
                    if (!string.IsNullOrEmpty(nextStep.Thought))
                    {
                        await OnStepExecute?.Invoke(StepTrace.Thought(goal, nextStep.Thought, _agentExecutionContext.GetStepId(), _agentExecutionContext.GetMessageId()));
                    }

                    await TryGetActionObservationAsync(_kernel, nextStep, chatHistory, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    if (!string.IsNullOrEmpty(nextStep.FinalAnswer))
                    {
                        await OnStepExecute?.Invoke(StepTrace.StepDone(_agentExecutionContext.GetMessageId()));
                        return nextStep.FinalAnswer;
                    }

                    if (!string.IsNullOrEmpty(nextStep.Thought))
                    {
                        chatHistory.AddAssistantMessage(nextStep.FormatThought());
                        await OnStepExecute?.Invoke(StepTrace.Thought(goal, nextStep.Thought, _agentExecutionContext.GetStepId(), _agentExecutionContext.GetMessageId()));
                    }
                }
            }

            return string.Empty;
        }

        private async Task<string?> InvokeActionAsync(Kernel kernel, string actionName, Dictionary<string, object> actionVariables, CancellationToken cancellationToken)
        {
            var availableFunctions = kernel.GetAvailableFunctions(x => !_config.ExcludedPlugins.Contains(x.PluginName) && !_config.ExcludedFunctions.Contains(x.GetFullyQualifiedFunctionName()));
            var targetFunction = availableFunctions.FirstOrDefault(f => f.GetFullyQualifiedFunctionName() == actionName);
            if (targetFunction == null)
            {
                this._logger?.LogDebug("Attempt to invoke action '{Action}' failed", actionName);
                return $"The tool '{actionName}' is not in [AVAILABLE FUNCTIONS]. Please try again using one of the [AVAILABLE FUNCTIONS].";
            }

            StepTrace _toolUseTrace = null;

            try
            {
                _stopwatch = Stopwatch.StartNew();
                var kernelFunction = kernel.GetKernelFunction(actionName);
                actionVariables = BindFunctionParameter(actionVariables, kernelFunction);

                var kernelArguments = new KernelArguments(actionVariables);
                kernelArguments = kernelArguments.MergeArguments(_config.Variables);

                if (_config.ToolCallMode == ToolCallMode.Async)
                {
                    _toolUseTrace = StepTrace.ToolUse(actionName, actionVariables, _agentExecutionContext.GetStepId(), _agentExecutionContext.GetMessageId());
                    await OnStepExecute?.Invoke(_toolUseTrace);
                }

                var kernelResult = await kernel.InvokeAsync(kernelFunction, kernelArguments, cancellationToken);
                var result = string.Empty;
                if (kernelResult.ValueType == typeof(string))
                {
                    result = kernelResult.GetValue<string>();
                }
                else
                {
                    result = JsonConvert.SerializeObject(kernelResult.GetValue<object>());
                }

                _stopwatch.Stop();
                this._logger?.LogTrace($"Invoked {actionName}. Result: {result}");
                if (_config.ToolCallMode == ToolCallMode.Sync)
                {
                    await OnStepExecute?.Invoke(StepTrace.ToolCall(actionName, actionVariables, result, _stopwatch.Elapsed.TotalSeconds, true, _agentExecutionContext.GetStepId(), _agentExecutionContext.GetMessageId()));
                }
                else
                {
                    await OnStepExecute?.Invoke(StepTrace.ToolResult(_toolUseTrace, actionName, actionVariables, result, _stopwatch.Elapsed.TotalSeconds, true));
                }

                return result;
            }
            catch (Exception e)
            {
                _stopwatch.Stop();
                if (_config.ToolCallMode == ToolCallMode.Sync)
                {
                    await OnStepExecute?.Invoke(StepTrace.ToolCall(actionName, actionVariables, e.Message, _stopwatch.Elapsed.TotalSeconds, false, _agentExecutionContext.GetStepId(), _agentExecutionContext.GetMessageId()));
                }
                else
                {
                    await OnStepExecute?.Invoke(StepTrace.ToolResult(_toolUseTrace, actionName, actionVariables, e.Message, _stopwatch.Elapsed.TotalSeconds, false));
                }

                this._logger?.LogError(e, "Something went wrong in system step: {Plugin}.{Function}. Error: {Error}", targetFunction.PluginName, targetFunction.Name, e.Message);
                throw;
            }
        }

        private async Task<bool> TryGetActionObservationAsync(Kernel kernel, ReasoningStep step, ChatHistory chatHistory, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(step.Action))
            {
                this._logger?.LogInformation("[ACTION] {Action}({ActionVariables}).", step.Action, JsonSerializerExtensions.Serialize(step.ActionVariables));

                // Add <Thought> and <Action> to chat history using XML format
                var messageBuilder = new StringBuilder();
                if (!string.IsNullOrEmpty(step.Thought))
                    messageBuilder.AppendLine(step.FormatThought());

                messageBuilder.AppendLine(step.FormatAction());

                chatHistory.AddAssistantMessage(messageBuilder.ToString());

                // Invoke Tool
                try
                {
                    var result = await InvokeActionAsync(kernel, step.Action, step.ActionVariables, cancellationToken).ConfigureAwait(false);
                    step.Observation = string.IsNullOrEmpty(result) ? $"There is no result can be found from tool call '{step.Action}'." : result!;
                }
                catch (Exception ex)
                {
                    step.Observation = $"An error occurs when calling tool '{step.Action}': {ex.Message}";
                    this._logger?.LogWarning(ex, "An error occurs when calling tool '{Action}'", step.Action);
                }

                this._logger?.LogInformation("[OBSERVATION] {Observation}", step.Observation);
                chatHistory.AddUserMessage(step.FormatObservation());

                return true;
            }

            return false;
        }







        private IAIService GetAIService(Kernel kernel)
        {
            var chatCompletionService = kernel.Services.GetService<IChatCompletionService>();
            if (chatCompletionService == null)
            {
                var textGenerationService = kernel.Services.GetService<ITextGenerationService>();
                return textGenerationService;
            }

            return chatCompletionService;
        }

        private async Task<ReasoningStep> GetNextStepAsync(List<ReasoningStep> stepsTaken, ChatHistory chatHistory, IAIService aiService, int startingMessageCount, CancellationToken cancellationToken)
        {
            var actionText = await GetNextStepCompletionAsync(stepsTaken, chatHistory, aiService, startingMessageCount, cancellationToken).ConfigureAwait(false);
            return ReasoningStepParser.Parse(actionText);
        }

        private Task<string> GetNextStepCompletionAsync(List<ReasoningStep> stepsTaken, ChatHistory chatHistory, IAIService aiService, int startingMessageCount, CancellationToken cancellationToken)
        {
            var skipStart = startingMessageCount;
            var skipCount = 0;

            var lastObservation = chatHistory.LastOrDefault(m => m.Content.StartsWith(ObservationTag, StringComparison.OrdinalIgnoreCase) || m.Content.Contains("<Observation"));
            var lastObservationIndex = lastObservation == null ? -1 : chatHistory.IndexOf(lastObservation);

            var messagesToKeep = lastObservationIndex >= 0 ? chatHistory.Count - lastObservationIndex : 0;

            string? originalThought = null;

            var reducedChatHistory = new ChatHistory();
            reducedChatHistory.AddRange(chatHistory.Where((m, i) => i < skipStart || i >= skipStart + skipCount));

            if (skipCount > 0 && originalThought is not null)
            {
                var skipedMessage = string.Format(CultureInfo.InvariantCulture, TrimMessageFormat, skipCount);
                reducedChatHistory.Insert(skipStart, new ChatMessageContent(AuthorRole.Assistant, skipedMessage));
                reducedChatHistory.Insert(skipStart, new ChatMessageContent(AuthorRole.Assistant, originalThought));
            }

            var addThought = stepsTaken.Count == 0;
            return GetStreamingCompletionAsync(aiService, reducedChatHistory, addThought, cancellationToken);
        }


        private async Task<string> GetStreamingCompletionAsync(IAIService aiService, ChatHistory chatHistory, bool addThought, CancellationToken cancellationToken)
        {
            var promptExecutionSettings = new PromptExecutionSettings() { FunctionChoiceBehavior = FunctionChoiceBehavior.None() };
            if (aiService is IChatCompletionService chatCompletionService)
            {
                var content = string.Empty;
                await foreach (var chatMessageContent in chatCompletionService.GetStreamingChatMessageContentsAsync(chatHistory, promptExecutionSettings, cancellationToken: cancellationToken))
                {
                    content += chatMessageContent.Content;
                }
                return content;
            }
            else if (aiService is ITextGenerationService textGenerationService)
            {
                var thoughtProcess = string.Join("\n", chatHistory.Select(m => m.Content));

                if (addThought)
                {
                    thoughtProcess = $"{thoughtProcess}\n{ThoughtTag}";
                    addThought = false;
                }

                thoughtProcess = $"{thoughtProcess}\n";

                var content = string.Empty;
                await foreach (var textContent in textGenerationService.GetStreamingTextContentsAsync(thoughtProcess, cancellationToken: cancellationToken))
                {
                    content += textContent.InnerContent.ToString();
                }
                return content;
            }

            throw new Exception("No available AIService for getting completions.");
        }

        private object GetValue(JsonElement element, Type returnType)
        {
            // boolean
            if (returnType == typeof(Boolean))
            {
                return Boolean.Parse(element.ToString());
            }

            // string 
            if (returnType == typeof(string))
            {
                return element.ToString();
            }

            // object
            if (returnType.BaseType != typeof(ValueType))
            {
                //return element;
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(element.ToString());
                //return JsonObject.Parse(element.ToString());
                //return JsonSerializer.Deserialize(element.ToString(), returnType);
            }

            // number
            var numberTypes = new List<Type>()
            {
                typeof(Int16), typeof(Int32), typeof(Int128),
                typeof(UInt16),typeof(UInt32), typeof(UInt128),
                typeof(int), typeof(short), typeof(long), typeof(float),typeof(double),
                typeof(decimal)
            };

            if (numberTypes.Contains(returnType))
            {
                return Convert.ChangeType(element.ToString(), returnType);
            }

            // null
            if (element.ValueKind == JsonValueKind.Null) return null;

            // array
            if (element.ValueKind == JsonValueKind.Array)
            {
                return System.Text.Json.JsonSerializer.Deserialize(element.ToString(), returnType);
            }

            return null;
        }

        private Dictionary<string, object> BindFunctionParameter(Dictionary<string, object> actionVariables, KernelFunction kernelFunction)
        {
            actionVariables = actionVariables ?? new Dictionary<string, object>();
            foreach (var parameter in kernelFunction.Metadata.Parameters)
            {
                if (actionVariables.ContainsKey(parameter.Name) && actionVariables[parameter.Name] is JsonElement)
                    actionVariables[parameter.Name] = GetValue((JsonElement)actionVariables[parameter.Name], parameter.ParameterType);
            }

            return actionVariables;
        }
    }
}
