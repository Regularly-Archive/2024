using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools;

/// <summary>
/// Asks the user for clarification or a decision.
/// </summary>
public class AskUserTool : ITool
{
    private readonly Func<string, string[]?, bool, Task<string>> _askHandler;

    public string Name => "ask_user";

    public ToolDefinition Definition { get; }

    public AskUserTool(Func<string, string[]?, bool, Task<string>> askHandler)
    {
        _askHandler = askHandler;
        Definition = new ToolDefinition
        {
            Name = Name,
            Description = "Ask the user a question to get clarification or make a decision. Supports a free-text Yes/No question, single choice, and multiple choice.",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    question = new
                    {
                        type = "string",
                        description = "The question to ask the user"
                    },
                    choices = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "List of options. If omitted, the client may provide a free-text or Yes/No response."
                    },
                    multiple_select = new
                    {
                        type = "boolean",
                        description = "Whether to allow multiple selection. Defaults to false."
                    }
                },
                required = new[] { "question" }
            })
        };
    }

    public async Task<ToolResult> ExecuteAsync(
        IDictionary<string, object> args,
        ToolExecutionContext context)
    {
        try
        {
            var arguments = new ToolArgumentReader(Definition.Schema, args);
            var question = arguments.GetString("question");
            arguments.TryGetStringArray("choices", out var choices);
            var multipleSelect = arguments.GetBoolean("multiple_select");

            var answer = await _askHandler(question, choices, multipleSelect);
            return ToolResult.FromText(answer);
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Failed to get user answer: {ex.Message}");
        }
    }
}
