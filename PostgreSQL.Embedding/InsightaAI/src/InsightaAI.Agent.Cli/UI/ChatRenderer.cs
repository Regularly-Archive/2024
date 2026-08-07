using InsightaAI.Agent.Cli.Localization;
using InsightaAI.Agent.Storage;
using Spectre.Console;

namespace InsightaAI.Agent.Cli.UI;

/// <summary>
/// 聊天界面渲染器 - 处理欢迎信息、历史消息、用户输入等
/// </summary>
public class ChatRenderer
{
    private const string RoleUser = "user";
    private const string RoleAssistant = "assistant";

    /// <summary>
    /// 显示欢迎信息
    /// </summary>
    public void ShowWelcome(string provider, string model, string sessionId, int toolCount, int skillCount = 0)
    {
        Terminal.Clear();
        AnsiConsole.Write(new FigletText("InsightaAI").Color(Color.Blue));
        AnsiConsole.MarkupLine($"[white]{CliStrings.Format("ChatWelcomeProviderModelFormat", provider, model)}[/]");
        AnsiConsole.MarkupLine($"[white]{CliStrings.Format("ChatWelcomeSessionIdFormat", sessionId)}[/]");
        AnsiConsole.MarkupLine($"[white]{CliStrings.Format("ChatWelcomeToolsSkillsFormat", toolCount, skillCount)}[/]");
        AnsiConsole.MarkupLine($"[white]{CliStrings.ChatWelcomeExitHint}[/]");
        AnsiConsole.MarkupLine($"[white]{CliStrings.ChatWelcomeClearHint}[/]");
        AnsiConsole.MarkupLine($"[white]{CliStrings.ChatWelcomeInputHint}[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// 显示历史消息
    /// </summary>
    public void ShowHistory(IEnumerable<MessageRecord> messages)
    {
        foreach (var msg in messages)
        {
            ShowMessage(msg);
        }
    }

    /// <summary>
    /// 显示单条消息
    /// </summary>
    public void ShowMessage(MessageRecord message)
    {
        var text = message.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? "";
        if (string.IsNullOrEmpty(text)) return;

        if (message.Role == RoleUser)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Markup("[bold green]> [/]");
            Console.WriteLine(text);
            AnsiConsole.WriteLine();
        }
        else if (message.Role == RoleAssistant)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Markup("[white]● [/]");
            Console.WriteLine(text);
            AnsiConsole.WriteLine();
        }
    }

    /// <summary>
    /// 提示用户输入（支持多行：粘贴换行不会触发提交，Shift+Enter 手动换行，Enter 发送）
    /// </summary>
    public string? PromptUser()
    {
        AnsiConsole.WriteLine();
        return new MultiLineTextPrompt().Show(AnsiConsole.Console);
    }

    /// <summary>
    /// 显示错误信息
    /// </summary>
    public void ShowError(string message)
    {
        AnsiConsole.MarkupLine($"[red]{CliStrings.ErrorPrefix}: {message}[/]");
    }

    /// <summary>
    /// 显示提示信息
    /// </summary>
    public void ShowInfo(string message)
    {
        AnsiConsole.MarkupLine($"[white]{message}[/]");
    }

    /// <summary>
    /// 显示警告信息
    /// </summary>
    public void ShowWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]{message}[/]");
    }

    /// <summary>
    /// 显示成功信息
    /// </summary>
    public void ShowSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[green]{message}[/]");
    }

    /// <summary>
    /// 显示会话列表
    /// </summary>
    public void ShowSessions(
        IReadOnlyList<SessionRecord> sessions,
        int pageNumber = 1,
        bool interactive = false,
        bool reachedEnd = false)
    {
        if (interactive)
        {
            Terminal.Clear();
        }

        if (sessions.Count == 0)
        {
            AnsiConsole.MarkupLine($"[grey]{CliStrings.SessionListEmpty}[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(CliStrings.SessionListFieldId)
            .AddColumn(CliStrings.SessionListFieldTitle)
            .AddColumn(CliStrings.SessionListFieldProvider)
            .AddColumn(CliStrings.SessionListFieldModel)
            .AddColumn(CliStrings.SessionListFieldMessages)
            .AddColumn(CliStrings.SessionListFieldCreatedAt);

        foreach (var s in sessions)
        {
            table.AddRow(
                s.Id,
                Markup.Escape(s.Title ?? "-"),
                s.Provider,
                s.Model,
                s.MessageCount.ToString(),
                s.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        }

        AnsiConsole.Write(table);
        if (interactive)
        {
            var previous = pageNumber > 1
                ? $"[blue]↑ {CliStrings.SessionListPrevious}[/]"
                : $"[grey]↑ {CliStrings.SessionListPrevious}[/]";
            var next = reachedEnd
                ? $"[grey]↓ {CliStrings.SessionListNext}[/]"
                : $"[blue]↓ {CliStrings.SessionListNext}[/]";
            var page = CliStrings.Format("SessionListPageFormat", pageNumber);
            AnsiConsole.MarkupLine($"[grey]{page}[/]  {previous}  {next}  [blue]Q {CliStrings.SessionListQuit}[/]");
        }
        AnsiConsole.MarkupLine($"[grey]{CliStrings.SessionListContinueHint}[/]");
    }
}
