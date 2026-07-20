using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;
using System.Text.Json;

namespace InsightaAI.Agent.Tools;

/// <summary>
/// 向用户提问工具 - 用于 Agent 需要澄清或获取信息时
/// 支持自由文本输入、单选和多选
/// </summary>
public class AskUserTool : ITool
{
    private readonly Func<string, string[]?, bool, Task<string>> _askHandler;

    public string Name => "ask_user";

    public ToolDefinition Definition { get; }

    /// <summary>
    /// 创建 AskUserTool
    /// </summary>
    /// <param name="askHandler">提问处理函数，接收 (问题, 选项列表, 是否多选)，返回用户回答</param>
    public AskUserTool(Func<string, string[]?, bool, Task<string>> askHandler)
    {
        _askHandler = askHandler;

        Definition = new ToolDefinition
        {
            Name = Name,
            Description = "Ask the user a question to get clarification or make a decision. Supports three modes:\n" +
                          "1. Yes/No question: don't pass choices, automatically shows Yes/No options\n" +
                          "2. Single choice: pass a choices array, user selects one option\n" +
                          "3. Multiple choice: pass a choices array with multiple_select=true, user can select multiple options",
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
                        description = "List of options. If not provided, defaults to [Yes, No]"
                    },
                    multiple_select = new
                    {
                        type = "boolean",
                        description = "Whether to allow multiple selection, default is false (single selection)"
                    }
                },
                required = new[] { "question", "choices" }
            })
        };
    }

    public async Task<ToolResult> ExecuteAsync(
        IDictionary<string, object> args,
        ToolExecutionContext context)
    {
        if (!args.TryGetValue("question", out var questionObj) || questionObj is not string question)
        {
            return ToolResult.FromError("Missing required parameter: question");
        }

        // 解析 choices
        string[]? choices = null;
        if (args.TryGetValue("choices", out var choicesObj) && choicesObj != null)
        {
            choices = choicesObj switch
            {
                JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Array =>
                    jsonElement.EnumerateArray()
                        .Select(x => x.GetString() ?? "")
                        .Where(x => !string.IsNullOrEmpty(x))
                        .ToArray(),
                string jsonStr => ParseJsonStringArray(jsonStr),
                string[] strArr => strArr,
                object[] objArr => objArr.Select(x => x?.ToString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToArray(),
                IEnumerable<object> objArr => objArr.Select(x => x?.ToString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToArray(),
                _ => null
            };
        }

        // 解析 multiple_select
        bool multipleSelect = false;
        if (args.TryGetValue("multiple_select", out var multiObj))
        {
            if (multiObj is JsonElement jsonElement)
            {
                multipleSelect = jsonElement.ValueKind == JsonValueKind.True;
            }
            else if (multiObj is bool boolVal)
            {
                multipleSelect = boolVal;
            }
        }

        try
        {
            var answer = await _askHandler(question, choices, multipleSelect);
            return ToolResult.FromText(answer);
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Failed to get user answer: {ex.Message}");
        }
    }

    private static string[]? ParseJsonStringArray(string json)
    {
        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(json);
            if (element.ValueKind == JsonValueKind.Array)
            {
                return element.EnumerateArray()
                    .Select(x => x.GetString() ?? "")
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToArray();
            }
        }
        catch
        {
            // 不是有效的 JSON
        }
        return null;
    }
}
