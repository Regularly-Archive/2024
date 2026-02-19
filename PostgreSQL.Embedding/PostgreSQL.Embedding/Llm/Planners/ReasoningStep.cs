using PostgreSQL.Embedding.Common.Json;
using System.Text.Json.Serialization;

namespace PostgreSQL.Embedding.Llm.Planners;

public class ReasoningStep
{
    [JsonPropertyName("thought")]
    public string Thought { get; set; }

    [JsonPropertyName("action")]
    public string Action { get; set; }

    [JsonPropertyName("observation")]
    public string Observation { get; set; }

    [JsonPropertyName("original_response")]
    public string RawResponse { get; set; }

    [JsonPropertyName("final_answer")]
    public string FinalAnswer => StructuredFinalAnswer.Content;

    [JsonPropertyName("structured_final_answer")]
    public StructuredAnswer StructuredFinalAnswer { get; set; } = new();

    [JsonPropertyName("action_variables")]
    public Dictionary<string, object> ActionVariables { get; set; }

    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>
    /// Format Thought as XML node: &lt;Thought Step="N"&gt;...&lt;/Thought&gt;
    /// </summary>
    public string FormatThought()
    {
        return string.IsNullOrEmpty(Thought)
            ? string.Empty
            : $"<Thought Step=\"{Index}\">{EscapeXml(Thought)}</Thought>";
    }

    /// <summary>
    /// Format Action as XML node: &lt;Action Step="N" Tool="PluginName.FunctionName"&gt;&lt;![CDATA[{...params...}]]&gt;&lt;/Action&gt;
    /// </summary>
    public string FormatAction()
    {
        if (string.IsNullOrEmpty(Action)) return string.Empty;

        var json = JsonSerializerExtensions.Serialize(ActionVariables);
        return $"<Action Step=\"{Index}\" Tool=\"{Action}\"><![CDATA[{json}]]></Action>";
    }

    /// <summary>
    /// Format Observation as XML node: &lt;Observation Step="N"&gt;...&lt;/Observation&gt;
    /// </summary>
    public string FormatObservation()
    {
        return string.IsNullOrEmpty(Observation)
            ? string.Empty
            : $"<Observation Step=\"{Index}\">{EscapeXml(Observation)}</Observation>";
    }

    private static string EscapeXml(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
