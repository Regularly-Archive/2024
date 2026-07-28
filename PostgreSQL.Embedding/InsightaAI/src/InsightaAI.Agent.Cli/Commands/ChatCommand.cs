using InsightaAI.Agent.Cli.Localization;
using InsightaAI.Agent.Cli.Services;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

namespace InsightaAI.Agent.Cli.Commands;

/// <summary>
/// chat 命令适配器。
/// 只负责定义命令行参数，并把执行委托给当前 Scope 中的 IChatApplication。
/// </summary>
public static class ChatCommand
{
    public static Command Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        var command = new Command("chat", CliStrings.ChatDescription);
        var sessionOption = new Option<string?>("--session", CliStrings.ChatSessionOption);
        var continueOption = new Option<bool>(new[] { "-c", "--continue" }, CliStrings.ChatContinueOption);
        command.AddOption(sessionOption);
        command.AddOption(continueOption);
        command.SetHandler(
            (sessionId, continueLast) => ExecuteInScopeAsync(scopeFactory, sessionId, continueLast),
            sessionOption,
            continueOption);
        return command;
    }

    private static async Task<int> ExecuteInScopeAsync(
        IServiceScopeFactory scopeFactory,
        string? sessionId,
        bool continueLast)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var application = scope.ServiceProvider.GetRequiredService<IChatApplication>();
        return await application.RunAsync(sessionId, continueLast);
    }
}
