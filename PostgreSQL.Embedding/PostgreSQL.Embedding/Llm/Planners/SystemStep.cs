using PostgreSQL.Embedding.Common.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PostgreSQL.Embedding.Llm.Planners
{
    public class SystemStep
    {
        private static readonly Regex s_thoughtRegex =
            new(@"(\[THOUGHT\])?(?<thought>.+?)(?=\[ACTION\]|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Regex s_finalAnswerRegex =
            new(@"\[FINAL[_\s\-]?ANSWER\](?<final_answer>.+)", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Regex s_actionAndActionVariablesRegex =
            new(@"\{[^{}]*""action""\s*:\s*""[^""]*"".*?""action_variables""\s*:\s*\{.*?\}.*?\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Regex s_actionOnlyRegex =
            new(@"\{[^{}]*""action""\s*:\s*""[^""]*"".*?\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private const string ActionTag = "[ACTION]";

        private const string ThoughtTag = "[THOUGHT]";
        private const string FinalAnswerTag = "[FINAL_ANSWER]";

        [JsonPropertyName("thought")]
        public string Thought { get; set; }

        [JsonPropertyName("action")]
        public string Action { get; set; }

        [JsonPropertyName("observation")]
        public string Observation { get; set; }

        [JsonPropertyName("original_response")]
        public string OriginalResponse { get; set; }

        [JsonPropertyName("final_answer")]
        public string FinalAnswer { get; set; }

        [JsonPropertyName("action_variables")]
        public Dictionary<string, object> ActionVariables { get; set; }

        public static SystemStep Parse(string input)
        {
            var result = new SystemStep { OriginalResponse = input };

            var finalAnswerMatch = s_finalAnswerRegex.Match(input);
            if (finalAnswerMatch.Success)
            {
                result.FinalAnswer = finalAnswerMatch.Groups[1].Value.Trim();
            }

            var thoughtMatch = s_thoughtRegex.Match(input);
            if (thoughtMatch.Success && !thoughtMatch.Value.Contains(ActionTag))
            {
                result.Thought = thoughtMatch.Value.Trim();

            }

            result.Thought = result.Thought?.Replace(ThoughtTag, string.Empty).Trim();
            result.FinalAnswer = result.FinalAnswer?.Replace(FinalAnswerTag, string.Empty).Trim();

            ExtractAction(input, result);

            if (!string.IsNullOrEmpty(result.Action) || !string.IsNullOrEmpty(result.Observation) || !string.IsNullOrEmpty(result.FinalAnswer))
            {
                result.Thought = null;
            }

            return result;
        }

        public override string ToString() => JsonSerializerExtensions.Serialize(this);

        private static void ExtractAction(string input, SystemStep step)
        {
            int actionIndex = input.IndexOf(ActionTag, StringComparison.OrdinalIgnoreCase);
            if (actionIndex != -1)
            {
                int jsonStartIndex = input.IndexOf("{", actionIndex, StringComparison.OrdinalIgnoreCase);
                if (jsonStartIndex != -1)
                {
                    int jsonEndIndex = input.Substring(jsonStartIndex).LastIndexOf("}", StringComparison.OrdinalIgnoreCase);
                    if (jsonEndIndex != -1)
                    {
                        string json = input.Substring(jsonStartIndex, jsonEndIndex + 1);

                        try
                        {
                            var actionStep = JsonSerializer.Deserialize<SystemStep>(json);

                            if (actionStep is not null)
                            {
                                step.Action = actionStep.Action;
                                step.ActionVariables = actionStep.ActionVariables ?? new Dictionary<string, object>();
                            }
                        }
                        catch (JsonException ex)
                        {
                            step.Observation = $"Unable to parse JSON string \n{json}\n, Exception: {ex.Message}.";
                        }
                    }
                }
            }
            else
            {
                var actionMatches = new List<Match>() { s_actionAndActionVariablesRegex.Match(input), s_actionOnlyRegex.Match(input) };

                var actionMatch = actionMatches.FirstOrDefault(x => x.Success == true);

                if (actionMatch != null)
                {
                    var json = actionMatch.Value.Trim();
                    try
                    {
                        var actionStep = JsonSerializer.Deserialize<SystemStep>(json);

                        if (actionStep is not null)
                        {
                            step.Action = actionStep.Action;
                            step.ActionVariables = actionStep.ActionVariables ?? new Dictionary<string, object>();
                        }
                    }
                    catch (JsonException ex)
                    {
                        step.Observation = $"Unable to parse JSON string \n{json}\n, Exception: {ex.Message}.";
                    }
                }
            }
        }
    }
}
