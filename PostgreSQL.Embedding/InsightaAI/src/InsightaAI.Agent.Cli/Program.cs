using System.CommandLine;
using System.Text;
using InsightaAI.Agent.Cli.Commands;
using InsightaAI.Agent.Storage;

namespace InsightaAI.Agent.Cli;

public class Program
{
    private static readonly IMessageStorage Storage = new JsonlMessageStorage();

    public static async Task<int> Main(string[] args)
    {
        // 设置控制台编码为 UTF-8（修复全局工具模式下特殊字符显示为问号的问题）
        Console.OutputEncoding = Encoding.UTF8;

        var rootCommand = new RootCommand("InsightaAI Agent CLI - Yet Another AI Agent");

        // 注册命令
        rootCommand.AddCommand(new ConfigCommand().Create());
        rootCommand.AddCommand(new ChatCommand(Storage).Create());
        rootCommand.AddCommand(new SessionsCommand(Storage).Create());
        rootCommand.AddCommand(new SkillsCommand().Create());
        rootCommand.AddCommand(new McpCommand().Create());

        // 如果第一个参数是选项（以 - 开头），自动补上 chat 子命令
        // 这样 insighta -c 等价于 insighta chat -c
        if (args.Length > 0 && args[0].StartsWith('-'))
        {
            args = ["chat", .. args];
        }

        // 如果没有子命令，默认运行 chat
        if (args.Length == 0)
        {
            return await new ChatCommand(Storage).ExecuteAsync(null);
        }

        return await rootCommand.InvokeAsync(args);
    }
}
