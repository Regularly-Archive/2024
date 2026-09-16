using InsightaAI.Agent.Cli.Run;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

namespace InsightaAI.Agent.Cli.Commands;

/// <summary>Registers the stable non-interactive <c>insighta run</c> process boundary.</summary>
public static class RunCommand
{
    public static Command Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        var command = new Command("run", "Run a non-interactive Agent task and emit JSONL events.");
        var taskArgument = new Argument<string?>("task", "Task text. Omit it when input is piped through stdin.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var sessionOption = new Option<string?>("--session", "Resume this main session.");
        var profileOption = new Option<string?>("--profile", "Use a global Insighta subagent profile.");
        var allowedToolsOption = new Option<string?>("--allowed-tools",
            "Comma-separated tool names to retain from the selected profile.");

        command.AddArgument(taskArgument);
        command.AddOption(sessionOption);
        command.AddOption(profileOption);
        command.AddOption(allowedToolsOption);
        command.SetHandler(
            (task, sessionId, profileId, allowedTools) => ExecuteAsync(
                scopeFactory, task, sessionId, profileId, ParseAllowedTools(allowedTools)),
            taskArgument,
            sessionOption,
            profileOption,
            allowedToolsOption);

        return command;
    }

    private static async Task<int> ExecuteAsync(
        IServiceScopeFactory scopeFactory,
        string? task,
        string? sessionId,
        string? profileId,
        IReadOnlyList<string>? allowedTools)
    {
        if (string.IsNullOrWhiteSpace(task))
        {
            if (!Console.IsInputRedirected)
                return await WriteUsageFailureAsync("Task text is required. Provide <task> or pipe it through standard input.");
            task = await Console.In.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(task))
            return await WriteUsageFailureAsync("Task text read from standard input is empty.");

        await using var scope = scopeFactory.CreateAsyncScope();
        var application = scope.ServiceProvider.GetRequiredService<RunApplication>();
        var result = await application.ExecuteAsync(new RunRequest
        {
            Input = task.Trim(),
            SessionId = sessionId,
            ProfileId = profileId,
            AllowedToolNames = allowedTools
        }, Console.Out);
        return result.ExitCode;
    }

    private static IReadOnlyList<string>? ParseAllowedTools(string? allowedTools)
    {
        if (allowedTools is null)
            return null;

        var names = allowedTools.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return names.Length > 0 ? names : [];
    }

    private static async Task<int> WriteUsageFailureAsync(string message)
    {
        await new RunJsonlWriter(Console.Out)
            .RunFailedAsync(null, message, exitCode: 2, CancellationToken.None);
        return 2;
    }
}
