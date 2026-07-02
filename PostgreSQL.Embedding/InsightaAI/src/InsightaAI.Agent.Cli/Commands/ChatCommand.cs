using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Cli.Hooks;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Cli.Services;
using InsightaAI.Agent.Cli.UI;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Extensions;
using InsightaAI.Agent.Mcp;
using InsightaAI.Agent.Mcp.Local;
using InsightaAI.Agent.Memory;
using InsightaAI.Agent.MetaLearning;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Skills;
using InsightaAI.Agent.Skills.Local;
using InsightaAI.Agent.Storage;
using InsightaAI.Agent.Tools;
using InsightaAI.LLM.Abstractions;
using Spectre.Console;
using System.CommandLine;

namespace InsightaAI.Agent.Cli.Commands;

/// <summary>
/// chat 命令 - 处理对话逻辑
/// </summary>
public class ChatCommand
{
    private const string CommandExit = "/exit";
    private const string CommandQuit = "/quit";
    private const string CommandClear = "/clear";

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

        // 注入环境变量
        foreach (var (key, value) in config.Envs)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

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

        // 获取或创建会话（先获取 sessionId）
        var session = await GetOrCreateSessionAsync(sessionId, config);
        if (session == null) return 1;

        // 创建 Agent（传入 sessionId 以注册会话记忆钩子）
        var agent = await CreateAgentAsync(config, llmClient, toolRegistry, skillRegistry, mcpRegistry, session.SessionId);

        // 显示欢迎信息
        _renderer.ShowWelcome(config.Provider, config.Model, session.SessionId, toolRegistry.GetDefinitions().Length, availableSkills.Count);
        _renderer.ShowHistory(session.Messages);

        // 运行对话循环
        await RunChatLoopAsync(session, agent);

        _renderer.ShowInfo($"Session saved: {session.SessionId}");
        _renderer.ShowInfo($"Resume with: insighta chat --session {session.SessionId}");
        _renderer.ShowInfo("See you again!");
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

            if (userInput.StartsWith("/compact", StringComparison.OrdinalIgnoreCase))
            {
                await HandleCompactCommandAsync(userInput, agent, session);
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

    private async Task HandleCompactCommandAsync(string input, Agent agent, ChatSession session)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var strategy = parts.Length > 1 ? parts[1] : "auto";

        // 构建上下文
        var context = new AgentContext
        {
            ConversationId = session.SessionId,
            History = session.GetLlmHistory()
        };

        _renderer.ShowInfo($"[yellow]⟳[/] Compacting context ([dim]{strategy}[/])...");

        try
        {
            var result = await agent.CompactContextAsync(strategy, context);

            if (result != null)
            {
                // 同步压缩后的消息到会话
                var compactedMessages = result.RequestMessages?.ToList() ?? [];
                await session.ReplaceMessagesAsync(compactedMessages);

                var tokenDelta = result.PreCompactTokens - result.PostCompactTokens;
                var msgDelta = result.PreCompactMessages - result.PostCompactMessages;
                _renderer.ShowSuccess(
                    $"[green]\u2713[/] Compacted ([dim]{result.StrategyName}[/]): " +
                    $"{result.PreCompactMessages} \u2192 {result.PostCompactMessages} messages ([dim]-{msgDelta}[/]), " +
                    $"~{result.PreCompactTokens:N0} \u2192 ~{result.PostCompactTokens:N0} tokens ([dim]-{tokenDelta:N0}[/])");
            }
            else
            {
                _renderer.ShowInfo("[dim]Context is clean, nothing to compact.[/]");
            }
        }
        catch (Exception ex)
        {
            _renderer.ShowError($"[red]$([char]0x2717)[/] Compact failed: {ex.Message}");
        }
    }

    private async Task ExecuteAgentAsync(Agent agent, string userInput, AgentContext context, ChatSession session)
    {
        using var eventRenderer = new EventRenderer();

        try
        {
            var roundToolCalls = new List<ToolCallContent>();
            var roundToolResults = new List<(string ToolCallId, string ToolName, string Result, bool IsError)>();
            int textLengthBeforeRound = 0;

            await foreach (var agentEvent in agent.RunStreamAsync(userInput, context))
            {
                await eventRenderer.HandleEventAsync(agentEvent);

                if (agentEvent is AgentToolStartEvent toolStart)
                {
                    roundToolCalls.Add(new ToolCallContent
                    {
                        Id = toolStart.ToolCallId,
                        Name = toolStart.ToolName,
                        Arguments = toolStart.Arguments
                    });
                }
                else if (agentEvent is AgentToolEndEvent toolEnd)
                {
                    roundToolResults.Add((toolEnd.ToolCallId, toolEnd.ToolName, toolEnd.ResultPreview ?? string.Empty, toolEnd.IsError));
                }
                else if (agentEvent is AgentRoundEndEvent roundEnd)
                {
                    if (roundEnd.HasToolCalls)
                    {
                        // AgentRoundEndEvent 在工具执行前发射
                        // 先保存上一轮积累的工具调用和结果
                        if (roundToolCalls.Count > 0)
                        {
                            await session.AddAssistantWithToolCallsAsync(null, roundToolCalls);
                            foreach (var tr in roundToolResults)
                            {
                                await session.AddToolResultMessageAsync(tr.ToolCallId, tr.ToolName, tr.Result, tr.IsError);
                            }
                            roundToolCalls.Clear();
                            roundToolResults.Clear();
                        }

                        // 保存本轮助手文本
                        var fullText = eventRenderer.FullText;
                        var roundText = fullText.Length > textLengthBeforeRound
                            ? fullText[textLengthBeforeRound..]
                            : null;
                        if (!string.IsNullOrWhiteSpace(roundText))
                        {
                            await session.AddAssistantMessageAsync(roundText);
                        }
                        textLengthBeforeRound = fullText.Length;
                    }
                    else
                    {
                        // 最后一轮（无工具调用）：保存之前积累的工具调用和结果
                        if (roundToolCalls.Count > 0)
                        {
                            await session.AddAssistantWithToolCallsAsync(null, roundToolCalls);
                            foreach (var tr in roundToolResults)
                            {
                                await session.AddToolResultMessageAsync(tr.ToolCallId, tr.ToolName, tr.Result, tr.IsError);
                            }
                            roundToolCalls.Clear();
                            roundToolResults.Clear();
                        }
                    }
                }
                else if (agentEvent is AgentContextCompactedEvent compactedEvent
                    && compactedEvent.CompactedMessages is { Length: > 0 } compactedMessages)
                {
                    await session.ReplaceMessagesAsync(compactedMessages.ToList());
                }
            }

            // 流结束后，保存剩余的工具调用和结果（如果有）
            if (roundToolCalls.Count > 0)
            {
                await session.AddAssistantWithToolCallsAsync(null, roundToolCalls);
                foreach (var tr in roundToolResults)
                {
                    await session.AddToolResultMessageAsync(tr.ToolCallId, tr.ToolName, tr.Result, tr.IsError);
                }
            }

            // 保存最终回复（纯文本）
            var finalText = eventRenderer.FullText;
            if (finalText.Length > textLengthBeforeRound)
            {
                var remainingText = finalText[textLengthBeforeRound..];
                if (!string.IsNullOrWhiteSpace(remainingText))
                {
                    await session.AddAssistantMessageAsync(remainingText);
                }
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

    private static async Task<Agent> CreateAgentAsync(CliConfig config, ILlmClient llmClient, ToolRegistry toolRegistry, SkillRegistry skillRegistry, McpRegistry? mcpRegistry = null, string? sessionId = null)
    {
        // 生成或获取用户 ID
        var userId = GetOrCreateUserId();

        var agentConfig = new AgentConfig
        {
            Id = "cli-agent",
            Name = "InsightaAI CLI",
            SystemPrompt = config.SystemPrompt,
            Model = config.Model,
            MaxToolRounds = config.MaxToolRounds,
            UserId = userId,
        };

        // 创建会话记忆钩子（需先于 ContextManager，供 SessionMemoryCompactStrategy 使用）
        SessionMemoryHook? sessionMemoryHook = null;
        if (!string.IsNullOrEmpty(sessionId))
        {
            var memoryOptions = new SessionMemoryOptions { SummaryModel = config.Model };
            sessionMemoryHook = new SessionMemoryHook(sessionId, userId, options: memoryOptions);
        }

        // 创建上下文管理器
        var contextManager = CreateContextManager(config, llmClient, sessionMemoryHook);

        // 创建记忆系统
        var memoryManager = CreateMemoryManager();

        var agent = new Agent(agentConfig, llmClient, toolRegistry, skillRegistry, mcpRegistry, contextManager, memoryManager);
        agent.AddHook(new ToolPermissionHook("bash", "write_file", "read_file", "edit_file"));

        // 注册元学习 Hook（自动捕获工具错误并记录教训）
        var metaLearningStore = new MetaLearningStore();
        await metaLearningStore.EnsureInitializedAsync();
        agent.AddHook(new MetaLearningHook(metaLearningStore));

        // 注册会话记忆钩子（短期记忆）
        if (sessionMemoryHook != null)
        {
            agent.AddAgentHook(sessionMemoryHook);
        }

        return agent;
    }

    private static IMemoryManager CreateMemoryManager()
    {
        var provider = new FileMemoryProvider();
        return new MemoryManager(provider);
    }

    private static string GetOrCreateUserId()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".insightai");
        Directory.CreateDirectory(configDir);

        var userIdFile = Path.Combine(configDir, "user_id");
        if (File.Exists(userIdFile))
        {
            return File.ReadAllText(userIdFile).Trim();
        }

        // 使用 12 个十六进制字符（48 bits），降低碰撞风险
        var userId = $"user_{Guid.NewGuid().ToString("N")[..12]}";
        File.WriteAllText(userIdFile, userId);
        return userId;
    }

    private static IContextManager? CreateContextManager(CliConfig config, ILlmClient llmClient, SessionMemoryHook? sessionMemoryHook = null)
    {
        // 从配置或模型名称获取上下文窗口大小
        var contextWindowTokens = ModelContextWindows.GetContextWindowSize(config.Model);

        var budget = new ContextBudget
        {
            MaxContextTokens = contextWindowTokens,
            Enabled = true
        };

        var tokenEstimator = new CharTokenEstimator();
        var strategies = new List<ICompactStrategy>
        {
            new MicroCompactStrategy()
        };

        // 注册会话记忆压缩策略（零 LLM 成本，优先级 2）
        if (sessionMemoryHook != null)
        {
            strategies.Add(new SessionMemoryCompactStrategy(sessionMemoryHook));
        }

        // 传统 LLM 摘要压缩（兜底，优先级 3）
        strategies.Add(new TraditionalCompactStrategy(llmClient, config.Model, config.SummaryModel));

        return new ContextManager(tokenEstimator, budget, strategies);
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
