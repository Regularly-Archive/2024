using InsightaAI.Agent.Cli.Commands;
using InsightaAI.Agent.Cli.Localization;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Cli.Services;
using InsightaAI.Agent.Storage;
using InsightaAI.Agents.Subagents.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System.CommandLine;
using System.Text;

namespace InsightaAI.Agent.Cli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // 设置控制台编码为 UTF-8（修复全局工具模式下特殊字符显示为问号的问题）
        Console.OutputEncoding = Encoding.UTF8;
        var cliConfig = CliConfig.Load();
        var bootstrap = CliBootstrap.Initialize(cliConfig);
        CliCulture.Configure(bootstrap.Language);

        // 初始化文件日志（~/.insighta/logs/insighta-{date}.log）
        InitLogger();

        // 创建 CLI 根 Host。命令行解析仍由 System.CommandLine 负责，
        // 共享基础设施和命令对象由 Host DI 管理。
        // 注：Telemetry 已改为会话级懒加载（见 ChatApplication.ExecuteAsync），
        // 仅进入 chat 会话时初始化 OTLP，管理命令（--help/config 等）零遥测开销。
        var hostBuilder = Host.CreateApplicationBuilder(args);
        hostBuilder.Logging.ClearProviders();
        hostBuilder.Logging.AddSerilog(Log.Logger, dispose: false);
        hostBuilder.Services.AddSingleton(cliConfig);
        hostBuilder.Services.AddSingleton(bootstrap);
        hostBuilder.Services.AddSingleton<IMessageStorage, JsonlMessageStorage>();
        hostBuilder.Services.AddScoped<IAgentFactory, AgentFactory>();
        hostBuilder.Services.AddScoped<IChatApplication, ChatApplication>();
        hostBuilder.Services.AddScoped<SessionsCommand>();
        hostBuilder.Services.AddSingleton<ISubagentDefinitionStore, LocalSubagentDefinitionStore>();
        hostBuilder.Services.AddSingleton<SubagentsCommand>();

        using var host = hostBuilder.Build();
        var scopeFactory = host.Services.GetRequiredService<IServiceScopeFactory>();

        var rootCommand = new RootCommand("InsightaAI Agent CLI - Yet Another AI Agent");

        // 注册命令
        rootCommand.AddCommand(new ConfigCommand().Create());
        rootCommand.AddCommand(ChatCommand.Create(scopeFactory));
        rootCommand.AddCommand(SessionsCommand.Create(scopeFactory));
        rootCommand.AddCommand(new SkillsCommand().Create());
        rootCommand.AddCommand(new McpCommand().Create());
        rootCommand.AddCommand(host.Services.GetRequiredService<SubagentsCommand>().Create());

        // 如果第一个参数是选项（以 - 开头），自动补上 chat 子命令
        // 这样 insighta -c 等价于 insighta chat -c
        if (args.Length > 0 && args[0].StartsWith('-'))
        {
            args = ["chat", .. args];
        }

        // 如果没有子命令，默认运行 chat
        if (args.Length == 0)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var application = scope.ServiceProvider.GetRequiredService<IChatApplication>();
            return await application.RunAsync(null);
        }

        return await rootCommand.InvokeAsync(args);
    }

    private static void InitLogger()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".insighta", "logs");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("InsightaAI.Agent.Memory.MemoryManager", LogEventLevel.Debug)
            .WriteTo.File(
                Path.Combine(logDir, ".log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Log.CloseAndFlush();
    }
}
