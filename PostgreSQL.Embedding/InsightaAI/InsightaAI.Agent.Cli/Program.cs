using System.CommandLine;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Cli.Services;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Tools.BuiltIn;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using Spectre.Console;

namespace InsightaAI.Agent.Cli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("InsightaAI Agent CLI - LLM 对话工具");

        // config 命令
        var configCommand = new Command("config", "配置 LLM 提供商和 API Key");
        configCommand.SetHandler(() => RunConfigAsync());

        // chat 命令（默认）
        var chatCommand = new Command("chat", "开始对话");
        var sessionOption = new Option<string?>("--session", "指定会话 ID（继续已有会话）");
        chatCommand.AddOption(sessionOption);
        chatCommand.SetHandler((session) => RunChatAsync(session), sessionOption);

        // sessions 命令
        var sessionsCommand = new Command("sessions", "查看历史会话");
        sessionsCommand.SetHandler(() => RunSessionsAsync());

        rootCommand.AddCommand(configCommand);
        rootCommand.AddCommand(chatCommand);
        rootCommand.AddCommand(sessionsCommand);

        // 如果没有子命令，默认运行 chat
        if (args.Length == 0)
        {
            return await RunChatAsync(null);
        }

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task RunConfigAsync()
    {
        var config = CliConfig.Load();

        AnsiConsole.MarkupLine("[bold blue]InsightaAI 配置[/]");
        AnsiConsole.WriteLine();

        // Provider 选择
        var provider = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("选择 LLM 提供商:")
                .AddChoices(new[] { "openai", "anthropic" }));
        config.Provider = provider;

        // Model 输入
        var defaultModel = provider == "openai" ? "gpt-4o-mini" : "claude-sonnet-4-20250514";
        config.Model = AnsiConsole.Prompt(
            new TextPrompt<string>($"模型名称:")
                .DefaultValue(defaultModel));

        // API Key 输入
        if (provider == "openai")
        {
            config.OpenAiApiKey = AnsiConsole.Prompt(
                new TextPrompt<string>("OpenAI API Key:")
                    .AllowEmpty()
                    .Secret());

            var baseUrl = AnsiConsole.Prompt(
                new TextPrompt<string>("OpenAI Base URL (可选，直接回车跳过):")
                    .AllowEmpty());
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                config.OpenAiBaseUrl = baseUrl;
            }
        }
        else
        {
            config.AnthropicApiKey = AnsiConsole.Prompt(
                new TextPrompt<string>("Anthropic API Key:")
                    .AllowEmpty()
                    .Secret());
        }

        // 系统提示词
        config.SystemPrompt = AnsiConsole.Prompt(
            new TextPrompt<string>("系统提示词:")
                .DefaultValue(config.SystemPrompt));

        config.Save();
        AnsiConsole.MarkupLine("[green]配置已保存到:[/] " + CliConfig.ConfigPath);
    }

    private static async Task<int> RunChatAsync(string? sessionId)
    {
        var config = CliConfig.Load();

        // 创建 LLM 客户端
        ILlmClient llmClient;
        try
        {
            llmClient = LlmClientFactory.Create(config);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]创建 LLM 客户端失败: {ex.Message}[/]");
            AnsiConsole.MarkupLine("[yellow]请先运行 'config' 命令配置 API Key[/]");
            return 1;
        }

        // 创建工具注册表并注册内置工具
        var toolRegistry = new ToolRegistry();
        toolRegistry.AddBuiltInTools();

        // 创建 Agent 配置
        var agentConfig = new AgentConfig
        {
            Id = "cli-agent",
            Name = "InsightaAI CLI",
            SystemPrompt = config.SystemPrompt,
            Model = config.Model,
            MaxToolRounds = config.MaxToolRounds
        };

        // 创建 Agent
        var agent = new InsightaAI.Agent.Agent(agentConfig, llmClient, toolRegistry);

        // 创建存储
        var storage = new JsonlStorage(sessionId);

        // 保存会话信息
        if (string.IsNullOrEmpty(sessionId))
        {
            storage.SaveSessionInfo(new SessionInfo
            {
                Id = storage.SessionId,
                Model = config.Model,
                Provider = config.Provider,
                CreatedAt = DateTime.UtcNow
            });
        }

        // 加载历史消息
        var messages = storage.LoadMessages();

        // 显示欢迎信息
        AnsiConsole.Clear();
        AnsiConsole.Write(new FigletText("InsightaAI").Color(Color.Blue));
        AnsiConsole.MarkupLine($"[grey]Provider: {config.Provider} | Model: {config.Model}[/]");
        AnsiConsole.MarkupLine($"[grey]Session: {storage.SessionId}[/]");
        AnsiConsole.MarkupLine($"[grey]Tools: {toolRegistry.GetDefinitions().Length} registered[/]");
        AnsiConsole.MarkupLine("[grey]输入消息开始对话，输入 'exit' 或 'quit' 退出[/]");
        AnsiConsole.MarkupLine("[grey]输入 'clear' 清空上下文[/]");
        AnsiConsole.WriteLine();

        // 显示历史消息
        foreach (var msg in messages)
        {
            DisplayMessage(msg);
        }

        // 对话循环
        while (true)
        {
            var userInput = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold green]You:[/]")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(userInput))
                continue;

            if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                userInput.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (userInput.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                messages.Clear();
                AnsiConsole.Clear();
                AnsiConsole.MarkupLine("[yellow]上下文已清空[/]");
                continue;
            }

            // 保存用户消息
            var userMessage = new SessionMessage
            {
                Role = "user",
                Content = userInput,
                Timestamp = DateTime.UtcNow
            };
            messages.Add(userMessage);
            storage.AppendMessage(userMessage);

            // 构建 Agent 上下文（包含历史消息）
            var history = messages.Select(m => m.Role switch
            {
                "user" => Message.FromUser(m.Content),
                "assistant" => Message.FromAssistant(m.Content),
                "system" => Message.FromSystem(m.Content),
                _ => Message.FromUser(m.Content)
            }).ToList();

            var context = new AgentContext
            {
                ConversationId = storage.SessionId,
                History = history
            };

            // 执行 Agent
            try
            {
                var fullResponse = "Assistant:";
                await foreach (var agentEvent in agent.RunStreamAsync(userInput, context))
                {
                    switch (agentEvent)
                    {
                        case AgentStartEvent agentStartEvent:
                            AnsiConsole.Write("Assistant: ");
                            break;
                        case AgentLlmStreamEvent llmEvent:
                            if (llmEvent.StreamEvent is TextDeltaEvent textDelta)
                            {
                                fullResponse += textDelta.Delta;
                                AnsiConsole.Write(textDelta.Delta);
                            }
                            if (llmEvent.StreamEvent is ThinkingDeltaEvent thinkingDelta)
                            {
                                fullResponse += thinkingDelta.Delta;
                                AnsiConsole.Write(thinkingDelta.Delta);
                            }
                            break;

                        case AgentToolStartEvent toolStart:
                            AnsiConsole.WriteLine();
                            AnsiConsole.MarkupLine($"[dim]Tool: {toolStart.ToolName}({toolStart.Arguments})[/]");
                            break;

                        case AgentToolEndEvent toolEnd:
                            var status = toolEnd.IsError ? "[red]Error[/]" : "[green]OK[/]";
                            AnsiConsole.MarkupLine($"[dim]{status}[/]");
                            break;

                        case AgentCompleteEvent complete:
                            if (!string.IsNullOrEmpty(fullResponse))
                            {
                                AnsiConsole.WriteLine();
                                var assistantMessage = new SessionMessage
                                {
                                    Role = "assistant",
                                    Content = fullResponse,
                                    Timestamp = DateTime.UtcNow
                                };
                                messages.Add(assistantMessage);
                                storage.AppendMessage(assistantMessage);
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]错误: {ex.Message}[/]");
            }
        }

        // 更新会话信息的 message count
        storage.SaveSessionInfo(new SessionInfo
        {
            Id = storage.SessionId,
            Model = config.Model,
            Provider = config.Provider,
            MessageCount = messages.Count,
            CreatedAt = storage.LoadSessionInfo()?.CreatedAt ?? DateTime.UtcNow
        });

        AnsiConsole.MarkupLine("[grey]会话已保存，再见！[/]");
        return 0;
    }

    private static void DisplayMessage(SessionMessage message)
    {
        if (message.Role == "user")
        {
            AnsiConsole.MarkupLine("[bold green]You:[/]");
            AnsiConsole.WriteLine(message.Content);
        }
        else if (message.Role == "assistant")
        {
            AnsiConsole.MarkupLine("[bold blue]Assistant:[/]");
            AnsiConsole.WriteLine(message.Content);
        }
    }

    private static async Task RunSessionsAsync()
    {
        var sessions = JsonlStorage.GetAllSessions();

        if (sessions.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]暂无历史会话[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("ID")
            .AddColumn("Provider")
            .AddColumn("Model")
            .AddColumn("Messages")
            .AddColumn("Created At");

        foreach (var session in sessions)
        {
            table.AddRow(
                session.Id,
                session.Provider,
                session.Model,
                session.MessageCount.ToString(),
                session.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("[grey]使用 'chat --session <id>' 继续已有会话[/]");
    }
}
