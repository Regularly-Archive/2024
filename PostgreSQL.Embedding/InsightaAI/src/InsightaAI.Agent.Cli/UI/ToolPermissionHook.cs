using InsightaAI.Agent.Cli.Extensions;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;
using Spectre.Console;

namespace InsightaAI.Agent.Cli.UI;

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

    private static string EscapeMarkup(string text)
    {
        return text.Replace("[", "[[").Replace("]", "]]");
    }
}
