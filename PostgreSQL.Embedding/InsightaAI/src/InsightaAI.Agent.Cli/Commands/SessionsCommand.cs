using System.CommandLine;
using InsightaAI.Agent.Cli.UI;
using InsightaAI.Agent.Storage;

namespace InsightaAI.Agent.Cli.Commands;

/// <summary>
/// sessions 命令 - 查看历史会话
/// </summary>
public class SessionsCommand
{
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
        var command = new Command("sessions", "查看历史会话");
        command.SetHandler(() => ExecuteAsync());
        return command;
    }

    /// <summary>
    /// 执行命令
    /// </summary>
    public async Task<int> ExecuteAsync()
    {
        var sessions = await _storage.GetSessionsAsync();
        _renderer.ShowSessions(sessions);
        return 0;
    }
}
