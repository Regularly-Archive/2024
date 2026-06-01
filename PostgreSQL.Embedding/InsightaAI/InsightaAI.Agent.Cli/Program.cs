using System.CommandLine;
using InsightaAI.Agent.Cli.Commands;
using InsightaAI.Agent.Storage;

namespace InsightaAI.Agent.Cli;

public class Program
{
    private static readonly IMessageStorage Storage = new JsonlMessageStorage();

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("InsightaAI Agent CLI - LLM 对话工具");

        // 注册命令
        rootCommand.AddCommand(new ConfigCommand().Create());
        rootCommand.AddCommand(new ChatCommand(Storage).Create());
        rootCommand.AddCommand(new SessionsCommand(Storage).Create());

        // 如果没有子命令，默认运行 chat
        if (args.Length == 0)
        {
            return await new ChatCommand(Storage).ExecuteAsync(null);
        }

        return await rootCommand.InvokeAsync(args);
    }
}
