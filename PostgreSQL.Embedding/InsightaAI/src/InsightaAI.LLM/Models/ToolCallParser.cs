using System.Text.Json;
using System.Text.RegularExpressions;

namespace InsightaAI.LLM.Models;

public static partial class ToolCallParser
{
    /// <summary>
    /// 从文本中移除 <tool_call>...</tool_call> 标签及其内容
    /// </summary>
    public static string StripToolCallTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return ToolCallContentRegex().Replace(text, "").Trim();
    }

    public static ToolCallBlock[] Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var toolCalls = new List<ToolCallBlock>();
        var toolCallMatches = ToolCallRegex().Matches(text);

        foreach (Match toolCallMatch in toolCallMatches)
        {
            var innerXml = toolCallMatch.Groups[1].Value;
            var functionMatch = FunctionRegex().Match(innerXml);
            if (!functionMatch.Success)
                continue;

            var functionName = functionMatch.Groups[1].Value;
            var parameters = new Dictionary<string, object>();
            var paramMatches = ParameterRegex().Matches(innerXml);

            foreach (Match paramMatch in paramMatches)
            {
                var paramName = paramMatch.Groups[1].Value;
                var paramValue = paramMatch.Groups[2].Value;
                parameters[paramName] = paramValue;
            }

            toolCalls.Add(new ToolCallBlock
            {
                Id = "call_" + Guid.NewGuid().ToString("N"),
                Name = functionName,
                Arguments = JsonSerializer.SerializeToElement(parameters)
            });
        }

        return toolCalls.ToArray();
    }

    [GeneratedRegex(@"<tool_call>(.*?)</tool_call>", RegexOptions.Singleline)]
    private static partial Regex ToolCallRegex();

    [GeneratedRegex(@"<tool_call>.*?</tool_call>\s*", RegexOptions.Singleline)]
    private static partial Regex ToolCallContentRegex();

    [GeneratedRegex(@"<function=(\w+)>")]
    private static partial Regex FunctionRegex();

    [GeneratedRegex(@"<parameter=(\w+)>\s*(.*?)\s*</parameter>")]
    private static partial Regex ParameterRegex();
}
