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
                // Not valid XML, return empty step with original response
            }

            return result;
        }

        /// <summary>
        /// Check if the input contains a FinalAnswer
        /// </summary>
        public static bool ContainsFinalAnswer(string input)
        {
            try
            {
                var wrappedInput = input.Contains("<Step") ? input : StepRoot.Replace("{input}", input);
                var doc = XDocument.Parse(wrappedInput);
                return doc.Root?.Element("FinalAnswer") != null;
            }
            catch (XmlException)
            {
                return false;
            }
        }

        /// <summary>
        /// Extract just the final answer content
        /// </summary>
        public static string? ExtractFinalAnswerContent(string input)
        {
            try
            {
                var wrappedInput = input.Contains("<Step") ? input : StepRoot.Replace("{input}", input);
                var doc = XDocument.Parse(wrappedInput);
                var finalAnswer = doc.Root?.Element("FinalAnswer");
                if (finalAnswer == null) return null;

                var content = finalAnswer.Element("Content")?.Value;
                return string.IsNullOrEmpty(content) ? finalAnswer.Value.Trim() : content.Trim();
            }
            catch (XmlException)
            {
                return null;
            }
        }
    }
}
