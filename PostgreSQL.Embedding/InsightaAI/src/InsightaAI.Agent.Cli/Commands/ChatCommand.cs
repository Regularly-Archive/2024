using InsightaAI.Agent;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Cli.Hooks;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Cli.Services;
using InsightaAI.Agent.Cli.UI;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Context.Compaction;
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
using InsightaAI.Agent.Tools.BuiltIn;
using InsightaAI.LLM.Abstractions;
using Microsoft.Extensions.DependencyInjection;
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
    private const string CommandModel = "/model";

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
        var auth = AuthConfig.Load();

        // 注入环境变量
        foreach (var (key, value) in config.Envs)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        if (!ValidateConfig(config, auth))
        {
            _renderer.ShowWarning("请先运行 'config' 命令进行配置");
            return 1;
        }

        // 解析当前模型配置
        var (providerName, _) = config.ParsePrimaryModel();
        var model = config.GetModel(config.PrimaryModel);

        // 创建 LLM 客户端
        ILlmClient llmClient;
        try
        {
            llmClient = LlmClientFactory.Create(auth, config);
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
        var session = await GetOrCreateSessionAsync(sessionId, config, providerName);
        if (session == null) return 1;

        // 创建 Agent（传入 sessionId 以注册会话记忆钩子）
        var agent = await CreateAgentAsync(config, auth, llmClient, model, toolRegistry, skillRegistry, mcpRegistry, session.SessionId);

        // 显示欢迎信息
        _renderer.ShowWelcome(providerName, model.ModelId, session.SessionId, toolRegistry.GetDefinitions().Length, availableSkills.Count);
        _renderer.ShowHistory(session.Messages);

        // 运行对话循环
        await RunChatLoopAsync(session, agent, config, auth, toolRegistry, skillRegistry, mcpRegistry);

        _renderer.ShowInfo($"Session saved: {session.SessionId}");
        _renderer.ShowInfo($"Resume with: insighta chat --session {session.SessionId}");
        _renderer.ShowInfo("See you again!");
        return 0;
    }

    private async Task RunChatLoopAsync(ChatSession session, Agent agent, CliConfig config, AuthConfig auth, ToolRegistry toolRegistry, SkillRegistry skillRegistry, McpRegistry? mcpRegistry)
    {
        var currentAgent = agent;

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
                await HandleCompactCommandAsync(userInput, currentAgent, session);
                continue;
            }

            if (userInput.StartsWith(CommandModel, StringComparison.OrdinalIgnoreCase))
            {
                currentAgent = await HandleModelSwitchAsync(userInput, currentAgent, config, auth, session, toolRegistry, skillRegistry, mcpRegistry);
                continue;
            }

            // 保存用户消息
            await session.AddUserMessageAsync(userInput);

            // 构建上下文
            var context = new AgentContext
            {
                SessionId = session.SessionId,
                History = session.GetLlmHistory()
            };

            // 执行 Agent
            await ExecuteAgentAsync(currentAgent, userInput, context, session);
        }
    }

    /// <summary>
    /// 处理 /model 命令 - 会话内切换模型
    /// </summary>
    private async Task<Agent> HandleModelSwitchAsync(string input, Agent currentAgent, CliConfig config, AuthConfig auth, ChatSession session, ToolRegistry toolRegistry, SkillRegistry skillRegistry, McpRegistry? mcpRegistry)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            _renderer.ShowWarning("用法: /model provider/model_key");
            _renderer.ShowInfo($"当前模型: {config.PrimaryModel}");
            if (config.Models.Count > 0)
            {
                _renderer.ShowInfo("可用模型:");
                foreach (var key in config.Models.Keys)
                {
                    var marker = key == config.PrimaryModel ? " ← current" : "";
                    _renderer.ShowInfo($"  {key}{marker}");
                }
            }
            return currentAgent;
        }

        var modelRef = parts[1];

        // 验证 model 引用格式
        string newProviderName, newModelKey;
        try
        {
            (newProviderName, newModelKey) = CliConfig.ParseModelReference(modelRef);
        }
        catch (InvalidOperationException ex)
        {
            _renderer.ShowError(ex.Message);
            return currentAgent;
        }

        // 验证 provider 存在
        if (!auth.Providers.ContainsKey(newProviderName))
        {
            _renderer.ShowError($"Provider '{newProviderName}' 未在 auth.json 中配置");
            return currentAgent;
        }

        // 验证 model 存在
        if (!config.Models.ContainsKey(modelRef))
        {
            _renderer.ShowError($"Model '{modelRef}' 未在 config.json 中配置");
            return currentAgent;
        }

        var newModel = config.Models[modelRef];

        // 创建新的 LLM 客户端
        ILlmClient newLlmClient;
        try
        {
            newLlmClient = LlmClientFactory.Create(auth, config, modelRef);
        }
        catch (Exception ex)
        {
            _renderer.ShowError($"创建 LLM 客户端失败: {ex.Message}");
            return currentAgent;
        }

        // 释放旧 agent
        currentAgent.Dispose();

        // 用新模型重建 Agent
        var newAgent = await CreateAgentAsync(config, auth, newLlmClient, newModel, toolRegistry, skillRegistry, mcpRegistry, session.SessionId);

        _renderer.ShowSuccess($"已切换到 {newProviderName}/{newModel.ModelId}");
        return newAgent;
    }

    private async Task HandleCompactCommandAsync(string input, Agent agent, ChatSession session)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var strategy = parts.Length > 1 ? parts[1] : "auto";

        // 构建上下文
        var context = new AgentContext
        {
            SessionId = session.SessionId,
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
            _renderer.ShowError($"[red]Compact failed: {ex.Message}[/] ");
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

    private static async Task<Agent> CreateAgentAsync(CliConfig config, AuthConfig auth, ILlmClient llmClient, ModelEntry model, ToolRegistry toolRegistry, SkillRegistry skillRegistry, McpRegistry? mcpRegistry = null, string? sessionId = null)
    {
        // 生成或获取用户 ID
        var userId = GetOrCreateUserId();

        var agentConfig = new AgentConfig
        {
            Id = "cli-agent",
            Name = "InsightaAI CLI",
            SystemPrompt = config.SystemPrompt,
            Model = model.ModelId,
            MaxTokens = model.MaxTokens,
            MaxToolRounds = config.MaxToolRounds,
            UserId = userId,
        };

        // 创建会话记忆钩子（需先于 ContextManager，供 SessionMemoryCompactStrategy 使用）
        SessionMemoryHook? sessionMemoryHook = null;
        if (!string.IsNullOrEmpty(sessionId))
        {
            var summaryModelId = config.ResolveSecondaryModelId();
            var memoryOptions = new SessionMemoryOptions { SummaryModel = summaryModelId ?? model.ModelId };
            sessionMemoryHook = new SessionMemoryHook(sessionId, userId, options: memoryOptions);
        }

        // 创建上下文管理器
        var contextManager = CreateContextManager(config, llmClient, model, sessionMemoryHook, toolRegistry);

        // 创建记忆系统
        var memoryManager = CreateMemoryManager();

        // 使用 AgentBuilder 构建 Agent
        var mcpConnectionPool = new SimpleMcpConnectionPool();
        var mcpRegistryToUse = mcpRegistry ?? new McpRegistry(mcpConnectionPool);

        var agent = new AgentBuilder(agentConfig)
            .WithLlm(llmClient)
            .WithToolRegistry(toolRegistry)
            .WithSkillRegistry(skillRegistry)
            .WithContextManager(contextManager!)
            .WithMemoryManager(memoryManager)
            .WithMcpRegistry(mcpRegistryToUse)
            .ConfigureServices(sp =>
            {
                sp.AddScoped<IFileSystem, LocalFileSystem>();
                sp.AddScoped<IShellExecutor, LocalShellExecutor>();
            })
            .Build();

        // 注册 Hook（Build 后添加）
        agent.AddHook(new ToolPermissionHook("bash", "write_file", "read_file", "edit_file", "web_fetch"));

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
            ".insighta");
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

    private static IContextManager? CreateContextManager(CliConfig config, ILlmClient llmClient, ModelEntry model, SessionMemoryHook? sessionMemoryHook = null, ToolRegistry? toolRegistry = null)
    {
        // 优先使用 model 配置的 context_window，否则从硬编码字典匹配
        var contextWindowTokens = model.ContextWindow > 0
            ? model.ContextWindow.Value
            : ModelContextWindows.GetContextWindowSize(model.ModelId);

        var budget = new ContextBudget
        {
            MaxContextTokens = contextWindowTokens,
            Enabled = true
        };

        var tokenEstimator = new CharTokenEstimator();
        var strategies = new List<ICompactStrategy>
        {
            new MicroCompactStrategy(toolRegistry)
        };

        // 注册会话记忆压缩策略（零 LLM 成本，优先级 2）
        if (sessionMemoryHook != null)
        {
            strategies.Add(new SessionMemoryCompactStrategy(sessionMemoryHook));
        }

        // 传统 LLM 摘要压缩（兜底，优先级 3）
        var summaryModelId = config.ResolveSecondaryModelId();
        strategies.Add(new TraditionalCompactStrategy(llmClient, model.ModelId, summaryModelId));

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

    private async Task<ChatSession?> GetOrCreateSessionAsync(string? sessionId, CliConfig config, string providerName)
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

        return await ChatSession.CreateAsync(_storage, config.PrimaryModel, providerName);
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

    private static bool ValidateConfig(CliConfig config, AuthConfig auth)
    {
        if (string.IsNullOrWhiteSpace(config.PrimaryModel))
            return false;

        try
        {
            var (providerName, _) = config.ParsePrimaryModel();

            if (!auth.Providers.ContainsKey(providerName))
                return false;

            if (!config.Models.ContainsKey(config.PrimaryModel))
                return false;
        }
        catch
        {
            return false;
        }

        return true;
    }
}
