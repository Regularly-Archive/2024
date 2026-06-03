using System.CommandLine;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Cli.Services;
using InsightaAI.Agent.Cli.UI;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Mcp;
using InsightaAI.Agent.Mcp.Local;
using InsightaAI.Agent.Skills;
using InsightaAI.Agent.Skills.Local;
using InsightaAI.Agent.Storage;
using InsightaAI.Agent.Tools;
using InsightaAI.Agent.Tools.BuiltIn;
using InsightaAI.LLM.Abstractions;
using Spectre.Console;

namespace InsightaAI.Agent.Cli.Commands;

/// <summary>
/// chat 命令 - 处理对话逻辑
/// </summary>
public class ChatCommand
{
    private const string CommandExit = "exit";
    private const string CommandQuit = "quit";
    private const string CommandClear = "clear";

    private readonly IMessageStorage _storage;
    private readonly ChatRenderer _renderer = new();

    public ChatCommand(IMessageStorage storage)
    {
        _storage = storage;
    }

    /// <summary>
    /// 创建命令对象
    /// </summary>
    public Command Create()
    {
        var command = new Command("chat", "开始对话");
        var sessionOption = new Option<string?>("--session", "指定会话 ID（继续已有会话）");
        command.AddOption(sessionOption);
        command.SetHandler((session) => ExecuteAsync(session), sessionOption);
        return command;
    }

    /// <summary>
    /// 执行命令
    /// </summary>
    public async Task<int> ExecuteAsync(string? sessionId)
    {
        var config = CliConfig.Load();

        if (!ValidateConfig(config))
        {
            _renderer.ShowWarning("请先运行 'config' 命令进行配置");
            return 1;
        }

        // 创建 LLM 客户端
        ILlmClient llmClient;
        try
        {
            llmClient = LlmClientFactory.Create(config);
        }
        catch (Exception ex)
        {
            _renderer.ShowError($"创建 LLM 客户端失败: {ex.Message}");
            return 1;
        }

        // 创建工具注册表
        var toolRegistry = CreateToolRegistry();

        // 创建 SkillRegistry
        var skillRegistry = CreateSkillRegistry();
        var availableSkills = await skillRegistry.ListAllSkillsAsync();

        // 创建 McpRegistry
        var mcpRegistry = CreateMcpRegistry();

        // 创建 Agent
        var agent = CreateAgent(config, llmClient, toolRegistry, skillRegistry, mcpRegistry);

        // 获取或创建会话
        var session = await GetOrCreateSessionAsync(sessionId, config);
        if (session == null) return 1;

        // 显示欢迎信息
        _renderer.ShowWelcome(config.Provider, config.Model, session.SessionId, toolRegistry.GetDefinitions().Length, availableSkills.Count);
        _renderer.ShowHistory(session.Messages);

        // 运行对话循环
        await RunChatLoopAsync(session, agent);

        _renderer.ShowInfo("会话已保存，再见！");
        return 0;
    }

    private async Task RunChatLoopAsync(ChatSession session, Agent agent)
    {
        while (true)
        {
            var userInput = _renderer.PromptUser();

            if (string.IsNullOrWhiteSpace(userInput))
                continue;

            AnsiConsole.WriteLine();

            if (userInput.Equals(CommandExit, StringComparison.OrdinalIgnoreCase) ||
                userInput.Equals(CommandQuit, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (userInput.Equals(CommandClear, StringComparison.OrdinalIgnoreCase))
            {
                await session.ClearAsync();
                AnsiConsole.Clear();
                _renderer.ShowWarning("上下文已清空");
                continue;
            }

            // 保存用户消息
            await session.AddUserMessageAsync(userInput);

            // 构建上下文
            var context = new AgentContext
            {
                ConversationId = session.SessionId,
                History = session.GetLlmHistory()
            };

            // 执行 Agent
            await ExecuteAgentAsync(agent, userInput, context, session);
        }
    }

    private async Task ExecuteAgentAsync(Agent agent, string userInput, AgentContext context, ChatSession session)
    {
        using var eventRenderer = new EventRenderer();

        try
        {
            await foreach (var agentEvent in agent.RunStreamAsync(userInput, context))
            {
                await eventRenderer.HandleEventAsync(agentEvent);
            }

            // 保存助手消息
            if (!string.IsNullOrEmpty(eventRenderer.FullText))
            {
                await session.AddAssistantMessageAsync(eventRenderer.FullText);
            }
        }
        catch (Exception ex)
        {
            _renderer.ShowError(ex.Message);
        }
    }

    private ToolRegistry CreateToolRegistry()
    {
        var registry = new ToolRegistry();
        registry.AddBuiltInTools();

        // 注册 ask_user 工具
        registry.Register(new AskUserTool(async (question, choices, multipleSelect) =>
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[yellow]●[/] Insighta wants to ask you: {question}");
            AnsiConsole.WriteLine();

            // 如果没有提供选项，默认使用 Yes/No
            var options = choices is { Length: > 0 } ? choices : ["Yes", "No"];


            if (multipleSelect)
            {
                // 多选模式
                var selected = AnsiConsole.Prompt(
                    new MultiSelectionPrompt<string>()
                        .Title("选择一个或多个选项（空格选择，回车确认）：")
                        .NotRequired()
                        .AddChoices(options));

                return selected.Count > 0 ? string.Join(", ", selected) : "(无选择)";
            }
            else
            {
                // 单选模式
                var selected = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("选择一个选项：")
                        .AddChoices(options));

                return selected;
            }
        }));

        return registry;
    }

    private static Agent CreateAgent(CliConfig config, ILlmClient llmClient, ToolRegistry toolRegistry, SkillRegistry skillRegistry, McpRegistry? mcpRegistry = null)
    {
        var agentConfig = new AgentConfig
        {
            Id = "cli-agent",
            Name = "InsightaAI CLI",
            SystemPrompt = config.SystemPrompt,
            Model = config.Model,
            MaxToolRounds = config.MaxToolRounds
        };

        var agent = new Agent(agentConfig, llmClient, toolRegistry, skillRegistry, mcpRegistry);
        agent.AddHook(new ToolPermissionHook("bash", "write_file", "read_file"));

        return agent;
    }

    private static SkillRegistry CreateSkillRegistry()
    {
        var registry = new SkillRegistry();

        // 加载全局 Skills
        if (Directory.Exists(CliConfig.GlobalSkillsDir))
        {
            registry.RegisterProvider(new LocalSkillProvider(CliConfig.GlobalSkillsDir));
        }

        // 加载项目级 Skills
        if (Directory.Exists(CliConfig.ProjectSkillsDir))
        {
            registry.RegisterProvider(new LocalSkillProvider(CliConfig.ProjectSkillsDir));
        }

        return registry;
    }

    private async Task<ChatSession?> GetOrCreateSessionAsync(string? sessionId, CliConfig config)
    {
        if (!string.IsNullOrEmpty(sessionId))
        {
            var session = await ChatSession.LoadAsync(_storage, sessionId);
            if (session == null)
            {
                _renderer.ShowError($"会话 {sessionId} 不存在");
                return null;
            }
            return session;
        }

        return await ChatSession.CreateAsync(_storage, config.Model, config.Provider);
    }

    private static McpRegistry? CreateMcpRegistry()
    {
        var globalConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".agents",
            "mcp-servers.json");
        var projectConfigPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            ".insighta",
            "mcp-servers.json");

        var globalExists = File.Exists(globalConfigPath);
        var projectExists = File.Exists(projectConfigPath);

        if (!globalExists && !projectExists)
        {
            return null;
        }

        var pool = new SimpleMcpConnectionPool();
        var registry = new McpRegistry(pool);

        if (globalExists)
        {
            registry.RegisterProvider(new JsonMcpServerProvider(globalConfigPath));
        }

        if (projectExists)
        {
            registry.RegisterProvider(new JsonMcpServerProvider(projectConfigPath));
        }

        return registry;
    }

    private static bool ValidateConfig(CliConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Provider))
            return false;

        if (string.IsNullOrWhiteSpace(config.Model))
            return false;

        if (config.Provider == "openai" && string.IsNullOrWhiteSpace(config.OpenAiApiKey))
            return false;

        if (config.Provider == "anthropic" && string.IsNullOrWhiteSpace(config.AnthropicApiKey))
            return false;

        return true;
    }
}
