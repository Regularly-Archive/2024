using InsightaAI.Agent.Cli.Commands;
using InsightaAI.Agent.Cli.Localization;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Cli.Run;
using InsightaAI.Agent.Cli.Services;
using InsightaAI.Agent.Storage;
using InsightaAI.Agents.Subagents.Catalog;
using InsightaAI.LLM.Anthropic;
using InsightaAI.LLM.Extensions;
using InsightaAI.LLM.Gemini;
using InsightaAI.LLM.OpenAI;
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
        hostBuilder.Services.AddScoped<RunApplication>();
        hostBuilder.Services.AddScoped<SessionsCommand>();
        hostBuilder.Services.AddSingleton<ISubagentDefinitionStore, LocalSubagentDefinitionStore>();
        hostBuilder.Services.AddSingleton<SubagentsCommand>();

        // LLM 客户端工厂：带标准 resilience 管道（重试/断路器）的命名 HttpClient，
        // 所有 LLM 调用共享。重试日志仅写入文件（见 InitLogger），终端 UI 无感。
        var fileLoggerFactory = LoggerFactory.Create(builder =>
            builder.AddSerilog(Log.Logger, dispose: false));
        hostBuilder.Services.AddLlmClientFactory(factory =>
        {
            factory.RegisterAdapter(new OpenAIAdapter());
            factory.RegisterAdapter(new OpenAIResponseAdapter());
            factory.RegisterAdapter(new AnthropicAdapter());
            factory.RegisterAdapter(new GeminiAdapter());
        }, retryLogger: fileLoggerFactory.CreateLogger("InsightaAI.LLM.Resilience"));

        using var host = hostBuilder.Build();
        var scopeFactory = host.Services.GetRequiredService<IServiceScopeFactory>();

        var rootCommand = new RootCommand("InsightaAI Agent CLI - Yet Another AI Agent");

        // 注册命令
        rootCommand.AddCommand(new ConfigCommand().Create());
        rootCommand.AddCommand(ChatCommand.Create(scopeFactory));
        rootCommand.AddCommand(RunCommand.Create(scopeFactory));
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
            // LLM HTTP 常规请求噪音（Start/Sending/Received/End 与 Polly 成功 attempt）全部静默；
            // 重试仍可见：Polly v8 重试事件为 Warning，业务侧重试日志走 InsightaAI.LLM.Resilience(Warning)。
            .MinimumLevel.Override("System.Net.Http.HttpClient.LlmClient", LogEventLevel.Warning)
            .MinimumLevel.Override("Polly", LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(logDir, ".log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Log.CloseAndFlush();
    }
}
