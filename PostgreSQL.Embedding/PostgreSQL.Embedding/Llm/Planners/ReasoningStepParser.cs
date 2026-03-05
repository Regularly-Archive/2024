using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PostgreSQL.Embedding.Llm.Planners
{
    /// <summary>
    /// Parser for XML-formatted ReAct steps: &lt;Thought&gt;, &lt;Action&gt;, &lt;Observation&gt;, &lt;FinalAnswer&gt;
    /// </summary>
    public static class ReasoningStepParser
    {
        private const string StepRoot = "<Step>{input}</Step>";

        // Regex patterns for fallback parsing
        private static readonly Regex FinalAnswerPattern = new Regex(@"<FinalAnswer[^>]*>(.*?)</FinalAnswer>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex ContentPattern = new Regex(@"<Content[^>]*>(.*?)</Content>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex ConfidencePattern = new Regex(@"<Confidence[^>]*>(.*?)</Confidence>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex ThoughtPattern = new Regex(@"<Thought[^>]*>(.*?)</Thought>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex ActionPattern = new Regex(@"<Action[^>]*>(.*?)</Action>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex ObservationPattern = new Regex(@"<Observation[^>]*>(.*?)</Observation>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        /// <summary>
        /// Parse a raw LLM response into a SystemStep
        /// </summary>
        public static ReasoningStep Parse(string input)
        {
            var result = new ReasoningStep { RawResponse = input };

            try
            {
                // Wrap input with root element for valid XML
                var wrappedInput = input.Contains("<Step") ? input : StepRoot.Replace("{input}", input);
                var doc = XDocument.Parse(wrappedInput);

                // Parse FinalAnswer
                var finalAnswer = doc.Root?.Element("FinalAnswer");
                if (finalAnswer != null)
                {
                    var content = finalAnswer.Element("Content")?.Value ?? finalAnswer.Value;
                    content = ExtractValue(content);

                    var confidence = finalAnswer.Element("Confidence");
                    var level = confidence?.Attribute("Level")?.Value ?? "Medium";
                    var reason = confidence?.Value ?? "";

                    result.StructuredFinalAnswer = new StructuredAnswer
                    {
                        Content = content.Trim(),
                        Level = level,
                        Reason = reason.Trim()
                    };
                }

                // Parse Thought
                var thought = doc.Root?.Element("Thought");
                if (thought != null) result.Thought = thought.Value.Trim();

                // Parse Action
                var action = doc.Root?.Element("Action");
                if (action != null)
                {
                    result.Action = action.Attribute("Tool")?.Value ?? string.Empty;

                    // Parse JSON from action content (may be wrapped in CDATA)
                    var jsonContent = action.Value.Trim();
                    if (!string.IsNullOrEmpty(jsonContent))
                    {
                        // Handle CDATA wrapper
                        if (jsonContent.StartsWith("<![CDATA[") && jsonContent.EndsWith("]]>"))
                            jsonContent = jsonContent[9..^3];

                        try
                        {
                            var variables = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
                            result.ActionVariables = variables ?? new Dictionary<string, object>();
                        }
                        catch
                        {
                            // JSON parsing failed, continue without action variables
                            result.Observation = $"Unable to parse arguments of tool '{result.Action}' from JSON: {jsonContent}";
                        }
                    }
                }

                // Parse Observation
                var observation = doc.Root?.Element("Observation");
                if (observation != null) result.Observation = observation.Value.Trim();
            }
            catch (XmlException)
            {
                // Not valid XML (e.g., text + XML mixed), try regex fallback
                ParseWithRegex(input, result);
            }

            return result;
        }

        /// <summary>
        /// Extract value and handle CDATA wrapper
        /// </summary>
        private static string ExtractValue(string value)
        {
            if (value.StartsWith("<![CDATA[") && value.EndsWith("]]>"))
                return value[9..^3];
            return value;
        }

        /// <summary>
        /// Fallback parsing using regex when XML parsing fails
        /// </summary>
        private static void ParseWithRegex(string input, ReasoningStep result)
        {
            // Parse FinalAnswer
            var finalAnswerMatch = FinalAnswerPattern.Match(input);
            if (finalAnswerMatch.Success)
            {
                var finalAnswerContent = finalAnswerMatch.Groups[1].Value;

                // Extract Content
                var contentMatch = ContentPattern.Match(finalAnswerContent);
                var content = contentMatch.Success ? contentMatch.Groups[1].Value : finalAnswerContent;
                content = ExtractValue(content);

                // Extract Confidence
                var confidenceMatch = ConfidencePattern.Match(finalAnswerContent);
                var level = "Medium";
                var reason = "";

                if (confidenceMatch.Success)
                {
                    var confidenceContent = confidenceMatch.Groups[1].Value;
                    var levelMatch = Regex.Match(confidenceMatch.Value, @"Level\s*=\s[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                    level = levelMatch.Success ? levelMatch.Groups[1].Value : "Medium";
                    reason = ExtractValue(confidenceContent);
                }

                result.StructuredFinalAnswer = new StructuredAnswer
                {
                    Content = content.Trim(),
                    Level = level,
                    Reason = reason.Trim()
                };
            }

            // Parse Thought
            var thoughtMatch = ThoughtPattern.Match(input);
            if (thoughtMatch.Success)
                result.Thought = ExtractValue(thoughtMatch.Groups[1].Value).Trim();

            // Parse Action
            var actionMatch = ActionPattern.Match(input);
            if (actionMatch.Success)
            {
                var actionContent = ExtractValue(actionMatch.Groups[1].Value);
                var toolMatch = Regex.Match(actionMatch.Value, @"Tool\s*=\s[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                result.Action = toolMatch.Success ? toolMatch.Groups[1].Value : "";

                if (!string.IsNullOrEmpty(actionContent))
                {
                    try
                    {
                        var variables = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(actionContent);
                        result.ActionVariables = variables ?? new Dictionary<string, object>();
                    }
                    catch
                    {
                        result.Observation = $"Unable to parse arguments of tool '{result.Action}' from JSON: {actionContent}";
                    }
                }
            }

            // Parse Observation
            var observationMatch = ObservationPattern.Match(input);
            if (observationMatch.Success)
                result.Observation = ExtractValue(observationMatch.Groups[1].Value).Trim();
        }
    }
}
