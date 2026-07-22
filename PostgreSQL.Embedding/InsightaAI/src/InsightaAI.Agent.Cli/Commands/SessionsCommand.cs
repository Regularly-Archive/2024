using System.CommandLine;
using InsightaAI.Agent.Cli.Localization;
using InsightaAI.Agent.Cli.UI;
using InsightaAI.Agent.Storage;
using Spectre.Console;

namespace InsightaAI.Agent.Cli.Commands;

/// <summary>
/// sessions 命令 - 查看历史会话
/// </summary>
public class SessionsCommand
{
    private const int PageSize = 10;
    private readonly IMessageStorage _storage;
    private readonly ChatRenderer _renderer = new();

    public SessionsCommand(IMessageStorage storage)
    {
        _storage = storage;
    }

    /// <summary>
    /// 创建命令对象
    /// </summary>
    public Command Create()
    {
        var command = new Command("sessions", CliStrings.SessionsDescription);
        var listCommand = new Command("list", CliStrings.SessionsListDescription);
        listCommand.SetHandler(() => ListAsync());

        var deleteCommand = new Command("delete", CliStrings.SessionsDeleteDescription);
        var sessionIdOption = new Option<string?>("--sessionId", CliStrings.SessionsDeleteSessionIdOption)
        {
            IsRequired = true
        };
        deleteCommand.AddOption(sessionIdOption);
        deleteCommand.SetHandler((sessionId) => DeleteAsync(sessionId), sessionIdOption);

        command.AddCommand(listCommand);
        command.AddCommand(deleteCommand);
        return command;
    }

    /// <summary>
    /// 执行命令
    /// </summary>
    public async Task<int> ListAsync()
    {
        var pageIndex = 0;
        var sessions = await GetPageAsync(pageIndex);

        if (Console.IsInputRedirected || Console.IsOutputRedirected || sessions.Count == 0)
        {
            _renderer.ShowSessions(sessions);
            return 0;
        }

        var reachedEnd = sessions.Count < PageSize;
        while (true)
        {
            _renderer.ShowSessions(sessions, pageIndex + 1, interactive: true, reachedEnd: reachedEnd);

            var key = Console.ReadKey(intercept: true).Key;
            if (key is ConsoleKey.Q or ConsoleKey.Escape)
            {
                break;
            }

            if (key is ConsoleKey.UpArrow or ConsoleKey.PageUp)
            {
                if (pageIndex == 0)
                {
                    continue;
                }

                pageIndex--;
                sessions = await GetPageAsync(pageIndex);
                reachedEnd = sessions.Count < PageSize;
                continue;
            }

            if (key is not (ConsoleKey.DownArrow or ConsoleKey.PageDown) || reachedEnd)
            {
                continue;
            }

            var nextPage = await GetPageAsync(pageIndex + 1);
            if (nextPage.Count == 0)
            {
                reachedEnd = true;
                continue;
            }

            pageIndex++;
            sessions = nextPage;
            reachedEnd = sessions.Count < PageSize;
        }

        return 0;
    }

    public async Task DeleteAsync(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _renderer.ShowError(CliStrings.SessionIdEmpty);
            return;
        }

        var session = await _storage.GetSessionAsync(sessionId);
        if (session == null)
        {
            _renderer.ShowWarning(CliStrings.Format("SessionNotFoundFormat", Markup.Escape(sessionId)));
            return;
        }

        await _storage.DeleteSessionAsync(sessionId);
        _renderer.ShowSuccess(CliStrings.Format("SessionDeletedFormat", Markup.Escape(sessionId)));
    }

    private Task<List<SessionRecord>> GetPageAsync(int pageIndex)
    {
        return _storage.GetSessionsAsync(offset: pageIndex * PageSize, limit: PageSize);
    }
}
