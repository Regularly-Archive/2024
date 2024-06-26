using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PostgreSQL.Embedding.Planners
{
    public class SystemStep
    {
        private static readonly Regex s_thoughtRegex =
            new(@"(\[THOUGHT\])?(?<thought>.+?)(?=\[ACTION\]|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Regex s_finalAnswerRegex =
            new(@"\[FINAL[_\s\-]?ANSWER\](?<final_answer>.+)", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private const string ActionTag = "[ACTION]";

        private const string ThoughtTag = "[THOUGHT]";

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
                return result;
            }

            var thoughtMatch = s_thoughtRegex.Match(input);
            if (thoughtMatch.Success)
            {
                if (!thoughtMatch.Value.Contains(ActionTag))
                {
                    result.Thought = thoughtMatch.Value.Trim();
                }
            }
            else if (!input.Contains(ActionTag))
            {
                result.Thought = input;
            }
            else
            {
                return result;
            }

            result.Thought = result.Thought?.Replace(ThoughtTag, string.Empty).Trim();

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
                            var systemStepResults = JsonSerializer.Deserialize<SystemStep>(json);

                            if (systemStepResults is not null)
                            {
                                result.Action = systemStepResults.Action;
                                result.ActionVariables = systemStepResults.ActionVariables;
                            }
                        }
                        catch (JsonException je)
                        {
                            result.Observation = $"Action parsing error: {je.Message}\nInvalid action: {json}";
                        }
                    }
                }
            }

            return result;
        }
    }
}
