using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Services;
using Microsoft.SemanticKernel.TextGeneration;
using PostgreSQL.Embedding.Common.Json;
using PostgreSQL.Embedding.LLmServices.Extensions;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PostgreSQL.Embedding.Planners
{
    public class StepwisePlan
    {
        private readonly string _userMessage;
        private readonly string _systemMessage;
        private readonly StepwisePlannerConfig _config;
        private readonly ILogger<StepwisePlan> _logger;

        private const string ObservationTag = "[OBSERVATION]";
        private const string ActionTag = "[ACTION]";
        private const string ThoughtTag = "[THOUGHT]";
        private const string TrimMessageFormat = "... I've removed the first {0} steps of my previous work to make room for the new stuff ...";
        private const string MainKey = "INPUT";

        private readonly Dictionary<string, string> _variables = new Dictionary<string, string>();

        public StepwisePlan(string systemMessage, string userMessage, StepwisePlannerConfig config, ILogger<StepwisePlan> logger)
        {
            _config = config;
            _userMessage = userMessage;
            _systemMessage = systemMessage;
            _logger = logger;
        }

        public async Task<string> ExecuteAsync(Kernel kernel, CancellationToken cancellationToken = default)
        {
            var stepsTaken = new List<SystemStep>();

            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(_systemMessage);
            chatHistory.AddUserMessage(_userMessage);

            var aiService = GetAIService(kernel);
            var startingMessageCount = chatHistory.Count;

            SystemStep? lastStep = null;

            for (var i = 0; i < _config.MaxIterations; i++)
            {
                if (i > 0) await Task.Delay(_config.MinIterationTimeMs, cancellationToken).ConfigureAwait(false);

                var nextStep = await GetNextStepAsync(stepsTaken, chatHistory, aiService, startingMessageCount, cancellationToken);
                _logger.LogTrace($"Step {i + 1}: {nextStep.ToString()}");

                var finalAnswer = TryGetFinalAnswer(nextStep, stepsTaken, i + 1);

                if (!string.IsNullOrEmpty(finalAnswer))
                    return finalAnswer;

                if (TryGetObservations(nextStep, chatHistory, stepsTaken, lastStep))
                    continue;

                nextStep = AddNextStep(nextStep, lastStep, chatHistory, stepsTaken, startingMessageCount);


                if (await TryGetActionObservationAsync(kernel, nextStep, chatHistory, cancellationToken).ConfigureAwait(false))
                    continue;

                _logger?.LogInformation("Action: No action to take");

                if (TryGetThought(nextStep, chatHistory))
                    continue;
            }

            AddExecutionStatsToContext(stepsTaken, _config.MaxIterations);
            _variables[MainKey] = "Result not found, review 'stepsTaken' to see what happened.";

            return string.Empty;
        }

        private bool TryGetThought(SystemStep step, ChatHistory chatHistory)
        {
            if (!string.IsNullOrEmpty(step.Thought))
                chatHistory.AddAssistantMessage($"{ThoughtTag} {step.Thought}");

            return false;
        }

        private async Task<string?> InvokeActionAsync(Kernel kernel, string actionName, Dictionary<string, object> actionVariables, CancellationToken cancellationToken)
        {
            var availableFunctions = kernel.GetAvailableFunctions(x => !_config.ExcludedPlugins.Contains(x.PluginName) && !_config.ExcludedPlugins.Contains(x.Name));
            var targetFunction = availableFunctions.FirstOrDefault(f => f.GetFullyQualifiedFunctionName() == actionName);
            if (targetFunction == null)
            {
                this._logger?.LogDebug("Attempt to invoke action {Action} failed", actionName);
                return $"{actionName} is not in [AVAILABLE FUNCTIONS]. Please try again using one of the [AVAILABLE FUNCTIONS].";
            }

            try
            {
                var kernelFunction = kernel.GetKernelFunction(actionName);

                var kernelArguments = new KernelArguments(actionVariables);
                foreach (var parameter in kernelFunction.Metadata.Parameters)
                {
                    if (actionVariables.ContainsKey(parameter.Name) && actionVariables[parameter.Name] is JsonElement)
                    {
                        actionVariables[parameter.Name] = GetValue((JsonElement)actionVariables[parameter.Name], parameter.ParameterType);
                    }
                }

                foreach (var varibale in _config.Variables)
                {
                    kernelArguments[varibale.Key] = varibale.Value;
                }

                var kernelResult = await kernel.InvokeAsync(kernelFunction, kernelArguments);
                var result = kernelResult.GetValue<string>();

                this._logger?.LogTrace($"Invoked {actionName}. Result: {result}");
                return result;
            }
            catch (Exception e)
            {
                this._logger?.LogError(e, "Something went wrong in system step: {Plugin}.{Function}. Error: {Error}", targetFunction.PluginName, targetFunction.Name, e.Message);
                throw;
            }
        }

        private async Task<bool> TryGetActionObservationAsync(Kernel kernel, SystemStep step, ChatHistory chatHistory, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(step.Action))
            {
                this._logger?.LogInformation("Action: {Action}({ActionVariables}).",
                    step.Action, JsonSerializerExtensions.Serialize(step.ActionVariables));

                // add [thought and] action to chat history
                var stringBuilder = new StringBuilder();
                stringBuilder.Append(ActionTag);
                var actionPayload = new { action = step.Action, action_variables = step.ActionVariables };
                stringBuilder.Append(JsonSerializerExtensions.Serialize(actionPayload));
                var actionMessage = stringBuilder.ToString();

                var message = string.IsNullOrEmpty(step.Thought) ? actionMessage : $"{ThoughtTag} {step.Thought}\n{actionMessage}";

                chatHistory.AddAssistantMessage(message);

                // Invoke the action
                try
                {
                    var result = await InvokeActionAsync(kernel, step.Action, step.ActionVariables, cancellationToken).ConfigureAwait(false);
                    step.Observation = string.IsNullOrEmpty(result) ? "Got no result from action" : result!;
                }
                catch (Exception ex)
                {
                    step.Observation = $"Error invoking action {step.Action} : {ex.Message}";
                    this._logger?.LogWarning(ex, "Error invoking action {Action}", step.Action);
                }

                this._logger?.LogInformation("Observation: {Observation}", step.Observation);
                chatHistory.AddUserMessage($"{ObservationTag} {step.Observation}");

                return true;
            }

            return false;
        }

        private SystemStep AddNextStep(SystemStep step, SystemStep lastStep, ChatHistory chatHistory, List<SystemStep> stepsTaken, int startingMessageCount)
        {
            // If the thought is empty and the last step had no action, copy action to last step and set as new nextStep
            if (string.IsNullOrEmpty(step.Thought) && lastStep is not null && string.IsNullOrEmpty(lastStep.Action))
            {
                lastStep.Action = step.Action;
                lastStep.ActionVariables = step.ActionVariables;

                lastStep.OriginalResponse += step.OriginalResponse;
                step = lastStep;
                if (chatHistory.Count > startingMessageCount)
                {
                    chatHistory.RemoveAt(chatHistory.Count - 1);
                }
            }
            else
            {
                _logger?.LogInformation("Thought: {Thought}", step.Thought);
                stepsTaken.Add(step);
                lastStep = step;
            }

            return step;
        }

        private bool TryGetObservations(SystemStep step, ChatHistory chatHistory, List<SystemStep> stepsTaken, SystemStep lastStep)
        {
            // If no Action/Thought is found, return any already available Observation from parsing the response.
            // Otherwise, add a message to the chat history to guide LLM into returning the next thought|action.
            if (string.IsNullOrEmpty(step.Action) &&
                string.IsNullOrEmpty(step.Thought))
            {
                // If there is an observation, add it to the chat history
                if (!string.IsNullOrEmpty(step.Observation))
                {
                    this._logger?.LogWarning("Invalid response from LLM, observation: {Observation}", step.Observation);
                    chatHistory.AddUserMessage($"{ObservationTag} {step.Observation}");
                    stepsTaken.Add(step);
                    lastStep = step;
                    return true;
                }

                if (lastStep is not null && string.IsNullOrEmpty(lastStep.Action))
                {
                    this._logger?.LogWarning("No response from LLM, expected Action");
                    chatHistory.AddUserMessage(ActionTag);
                }
                else
                {
                    this._logger?.LogWarning("No response from LLM, expected Thought");
                    chatHistory.AddUserMessage(ThoughtTag);
                }

                // No action or thought from LLM
                return true;
            }

            return false;
        }

        private void AddExecutionStatsToContext(List<SystemStep> stepsTaken, int iterations)
        {
            _variables["stepCount"] = stepsTaken.Count.ToString(CultureInfo.InvariantCulture);
            _variables["stepsTaken"] = JsonSerializer.Serialize(stepsTaken);
            _variables["iterations"] = iterations.ToString(CultureInfo.InvariantCulture);

            var actionCounts = new Dictionary<string, int>();
            foreach (var step in stepsTaken)
            {
                if (string.IsNullOrEmpty(step.Action)) { continue; }

                _ = actionCounts.TryGetValue(step.Action, out int currentCount);
                actionCounts[step.Action!] = ++currentCount;
            }

            var functionCallListWithCounts = string.Join(", ", actionCounts.Keys.Select(function =>
                $"{function}({actionCounts[function]})"));

            var functionCallTotalCount = actionCounts.Values.Sum().ToString(CultureInfo.InvariantCulture);

            _variables["functionCount"] = $"{functionCallTotalCount} ({functionCallListWithCounts})";
        }

        private string TryGetFinalAnswer(SystemStep step, List<SystemStep> stepsTaken, int iterations)
        {
            if (!string.IsNullOrEmpty(step.FinalAnswer))
            {
                _variables.Add("INPUT", step.FinalAnswer);
                stepsTaken.Add(step);

                AddExecutionStatsToContext(stepsTaken, iterations);

                return step.FinalAnswer;
            }

            return null;
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

        private async Task<SystemStep> GetNextStepAsync(List<SystemStep> stepsTaken, ChatHistory chatHistory, IAIService aiService, int startingMessageCount, CancellationToken cancellationToken)
        {
            var actionText = await GetNextStepCompletionAsync(stepsTaken, chatHistory, aiService, startingMessageCount, cancellationToken).ConfigureAwait(false);
            return SystemStep.Parse(actionText);
        }

        private Task<string> GetNextStepCompletionAsync(List<SystemStep> stepsTaken, ChatHistory chatHistory, IAIService aiService, int startingMessageCount, CancellationToken CancellationToken)
        {
            var skipStart = startingMessageCount;
            var skipCount = 0;

            var lastObservation = chatHistory.LastOrDefault(m => m.Content.StartsWith(ObservationTag, StringComparison.OrdinalIgnoreCase));
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
            return GetCompletionAsync(aiService, reducedChatHistory, addThought, CancellationToken);
        }

        private async Task<string> GetCompletionAsync(IAIService aiService, ChatHistory chatHistory, bool addThought, CancellationToken CancellationToken)
        {
            if (aiService is IChatCompletionService chatCompletionService)
            {
                var chatMessageContent = await chatCompletionService.GetChatMessageContentAsync(chatHistory);
                return (chatMessageContent.InnerContent as Azure.AI.OpenAI.ChatResponseMessage)?.Content;
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

                var textContent = await textGenerationService.GetTextContentAsync(thoughtProcess);
                return textContent.InnerContent.ToString();
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
                return JsonSerializer.Deserialize(element.ToString(), returnType);
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
                return JsonSerializer.Deserialize(element.ToString(), returnType);
            }

            return null;
        }
    }
}
