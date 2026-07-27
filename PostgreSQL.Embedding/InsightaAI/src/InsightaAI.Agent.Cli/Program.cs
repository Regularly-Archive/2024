using InsightaAI.Agent.Cli.Commands;
using InsightaAI.Agent.Cli.Localization;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Storage;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using System.CommandLine;
using System.Text;

namespace InsightaAI.Agent.Cli;

public class Program
{
    private static readonly IMessageStorage Storage = new JsonlMessageStorage();

    public static async Task<int> Main(string[] args)
    {
        // 设置控制台编码为 UTF-8（修复全局工具模式下特殊字符显示为问号的问题）
        Console.OutputEncoding = Encoding.UTF8;
        var language = Environment.GetEnvironmentVariable("INSIGHTA_LANGUAGE")
            ?? CliConfig.Load().Language;
        CliCulture.Configure(language);

        // 初始化文件日志（~/.insighta/logs/insighta-{date}.log）
        InitLogger();

        // 初始化 OpenTelemetry（通过环境变量 INSIGHTA_TELEMETRY=1 启用）
        using var telemetry = InitTelemetry();

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

    private static void InitLogger()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".insighta", "logs");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDir, ".log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private static IDisposable? InitTelemetry()
    {

        var endpoint = Environment.GetEnvironmentVariable("INSIGHTA_OTEL_ENDPOINT") ?? "http://localhost:4317";

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService("insighta-cli");

        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddSource("InsightaAI.Agent")
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => o.Endpoint = new Uri(endpoint))
            .Build();

        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddMeter("InsightaAI.Agent")
            .AddOtlpExporter(o => o.Endpoint = new Uri(endpoint))
            .Build();

        return new CompositeDisposable(tracerProvider, meterProvider);
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable[] _disposables;
        public CompositeDisposable(params IDisposable[] disposables) => _disposables = disposables;
        public void Dispose()
        {
            foreach (var d in _disposables) d.Dispose();
        }
    }
}
