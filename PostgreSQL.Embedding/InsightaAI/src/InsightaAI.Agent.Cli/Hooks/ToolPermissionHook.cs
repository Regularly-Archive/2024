using System.Text.Json;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Cli.Extensions;
using InsightaAI.Agent.Hooks;
using Spectre.Console;

namespace InsightaAI.Agent.Cli.Hooks;

/// <summary>
/// 工具权限确认钩子 - 在工具执行前向用户征求同意
/// </summary>
public class ToolPermissionHook : IToolHook
{
    private readonly List<string>? _targetTools;

    /// <summary>
    /// 创建工具权限确认钩子
    /// </summary>
    /// <param name="targetTools">需要确认的工具列表，为空则对所有工具生效</param>
    public ToolPermissionHook(params string[]? targetTools)
    {
        _targetTools = targetTools?.Length > 0 ? [.. targetTools] : null;
    }

    public IReadOnlyList<string>? TargetTools => _targetTools;

    public Task<ToolHookResult> OnBeforeExecutionAsync(
        string toolName,
        string arguments,
        ToolExecutionContext context)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[yellow]●[/] Insighta wants to use tool [cyan]{EscapeMarkup(toolName)}[/] with arguments:");

        var displayArgs = arguments.TruncateToConsoleWidth(offset: 4);
        AnsiConsole.MarkupLine($"[dim]⎿ {EscapeMarkup(displayArgs)}[/]");

        // 对 edit_file 工具显示 diff 预览
        if (toolName == "edit_file")
        {
            ShowEditDiff(arguments);
        }

        AnsiConsole.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Do you want to proceed?")
                .AddChoices([
                    "Yes",
                    "Yes, and don't ask again in current session",
                    "Reject"
                ]));

        return Task.FromResult(choice switch
        {
            "Yes" => ToolHookResult.Allow,
            "Yes, and don't ask again in current session" => ToolHookResult.AllowAlways,
            "Reject" => ToolHookResult.Deny,
            _ => ToolHookResult.Deny
        });
    }

    public Task OnAfterExecutionAsync(
        string toolName,
        ToolResult result,
        ToolExecutionContext context)
    {
        // 可以在这里添加执行后的逻辑，比如日志记录
        return Task.CompletedTask;
    }

    /// <summary>
    /// 为 edit_file 工具显示 inline diff 预览
    /// </summary>
    private static void ShowEditDiff(string arguments)
    {
        try
        {
            var doc = JsonDocument.Parse(arguments);
            var root = doc.RootElement;

            if (!root.TryGetProperty("old_string", out var oldElement) ||
                !root.TryGetProperty("new_string", out var newElement) ||
                !root.TryGetProperty("file_path", out var filePathElement))
                return;

            var oldText = oldElement.GetString() ?? "";
            var newText = newElement.GetString() ?? "";
            var filePath = filePathElement.GetString() ?? "";

            var diffBuilder = new InlineDiffBuilder(new Differ());
            var diffModel = diffBuilder.BuildDiffModel(oldText, newText);

            // 统计变更行数
            var added = diffModel.Lines.Count(l => l.Type == ChangeType.Inserted);
            var removed = diffModel.Lines.Count(l => l.Type == ChangeType.Deleted);

            if (added == 0 && removed == 0)
                return;

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[dim]File: {EscapeMarkup(filePath)}[/]");
            AnsiConsole.MarkupLine($"[dim]Changes Preview: [green]+{added}[/] lines, [red]-{removed}[/] lines[/]");

            var panelLines = new List<Markup>();
            foreach (var line in diffModel.Lines)
            {
                var escaped = EscapeMarkup(line.Text ?? "");
                var prefix = line.Type switch
                {
                    ChangeType.Inserted => "[green]+",
                    ChangeType.Deleted => "[red]-",
                    _ => "[dim] "
                };
                panelLines.Add(new Markup($"{prefix}{escaped}[/]"));
            }

            var panelContent = new Rows(panelLines);
            var panel = new Panel(panelContent)
            {
                Border = BoxBorder.Square,
                BorderStyle = new Style(Color.Grey),
                Padding = new Padding(0, 0, 0, 0)
            };
            AnsiConsole.Write(panel);
        }
        catch
        {
            // JSON 解析失败或 diff 生成异常时静默跳过
        }
    }

    private static string EscapeMarkup(string text)
    {
        return text.Replace("[", "[[").Replace("]", "]]");
    }
}
