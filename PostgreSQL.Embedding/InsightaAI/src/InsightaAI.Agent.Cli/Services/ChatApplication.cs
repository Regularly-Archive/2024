using InsightaAI.Agent;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Cli.Hooks;
using InsightaAI.Agent.Cli.Localization;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Cli.UI;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Context.Compaction;
using InsightaAI.Agent.Context.Summary;
using InsightaAI.Agent.Diagnostics;
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
using InsightaAI.Agents.Subagents.Invocation;
using InsightaAI.LLM.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Spectre.Console;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>
/// Chat 应用服务 - 处理对话业务流程
/// </summary>
public sealed class ChatApplication : IChatApplication
{
    private const string CommandExit = "/exit";
    private const string CommandQuit = "/quit";
    private const string CommandClear = "/clear";
    private const string CommandModel = "/model";
    private static readonly IReadOnlyList<SlashCommand> SlashCommands =
    [
        new(CommandModel, CliStrings.ChatSlashCommandModelDescription, AcceptsArgument: true),
        new("/compact", CliStrings.ChatSlashCommandCompactDescription, AcceptsArgument: true),
        new(CommandClear, CliStrings.ChatSlashCommandClearDescription),
        new(CommandExit, CliStrings.ChatSlashCommandExitDescription),
        new(CommandQuit, CliStrings.ChatSlashCommandQuitDescription)
    ];

    private readonly IMessageStorage _storage;
    private readonly IAgentFactory _agentFactory;
    private readonly CliConfig _config;
    private readonly CliBootstrap _bootstrap;
    private readonly ChatRenderer _renderer = new();

    public ChatApplication(
        IMessageStorage storage,
        IAgentFactory agentFactory,
        CliConfig config,
        CliBootstrap bootstrap)
    {
        _storage = storage;
        _agentFactory = agentFactory;
        _config = config;
        _bootstrap = bootstrap;
    }

    /// <summary>
    /// 执行命令
    /// </summary>
    public async Task<int> ExecuteAsync(string? sessionId, bool continueLast = false)
    {
        var config = _config;
        var auth = AuthConfig.Load();

        if (!ValidateConfig(config, auth))
        {
            _renderer.ShowWarning(CliStrings.ChatConfigRequiredHint);
            return 1;
        }

        // 会话级遥测懒加载：仅真正进入 chat 会话才初始化 OTLP exporter，
        // 随会话结束自动 dispose（见 AGENTS.md「Telemetry 会话级懒加载」）
        using var telemetry = InitTelemetry(_bootstrap);

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
            _renderer.ShowError(CliStrings.Format("ChatLlmClientFailedFormat", ex.Message));
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
        var session = await GetOrCreateSessionAsync(sessionId, continueLast, config, providerName);
        if (session == null) return 1;

        var summaryService = CreateSummaryService(config, auth);
        var userId = AgentFactory.GetOrCreateUserId();

        // 创建 Agent（传入 sessionId 以注册会话记忆钩子）
        var agentOptions = new AgentCreationOptions
        {
            Config = config,
            Auth = auth,
            LlmClient = llmClient,
            Model = model,
            ToolRegistry = toolRegistry,
            SkillRegistry = skillRegistry,
            SummaryService = summaryService,
            McpRegistry = mcpRegistry,
            SessionId = session.SessionId,
            UserId = userId
        };
        RegisterDelegationTool(toolRegistry, agentOptions);
        var agent = await _agentFactory.CreateAsync(agentOptions);

        // 显示欢迎信息
        _renderer.ShowWelcome(providerName, model.ModelId, session.SessionId, toolRegistry.GetDefinitions().Length, availableSkills.Count);
        _renderer.ShowHistory(session.Messages);

        // 运行对话循环
        await RunChatLoopAsync(session, agent, summaryService, config, auth, toolRegistry, skillRegistry, mcpRegistry, userId);

        _renderer.ShowInfo(CliStrings.Format("ChatSessionSavedFormat", session.SessionId));
        _renderer.ShowInfo(CliStrings.Format("ChatSessionResumeHintFormat", session.SessionId));
        _renderer.ShowInfo(CliStrings.ChatGoodbye);
        return 0;
    }

    /// <inheritdoc />
    public Task<int> RunAsync(string? sessionId, bool continueLast = false)
    {
        return ExecuteAsync(sessionId, continueLast);
    }

    /// <summary>
    /// 会话级遥测初始化。仅 chat 会话调用；OTLP exporter 设短超时，
    /// 避免无 collector 时 dispose/flush 连接挂起。
    /// </summary>
    private static IDisposable? InitTelemetry(CliBootstrap bootstrap)
    {
        if (!bootstrap.TelemetryEnabled)
            return null;

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService("insighta-cli");

        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddSource("InsightaAI.Agent")
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(bootstrap.OtlpEndpoint);
                o.TimeoutMilliseconds = 1000;
            })
            .Build();

        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddMeter("InsightaAI.Agent")
            .AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(bootstrap.OtlpEndpoint);
                o.TimeoutMilliseconds = 1000;
            })
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

    private async Task RunChatLoopAsync(ChatSession session, Agent agent, ISummaryService summaryService, CliConfig config, AuthConfig auth, ToolRegistry toolRegistry, SkillRegistry skillRegistry, McpRegistry? mcpRegistry, string userId)
    {
        var currentAgent = agent;

        while (true)
        {
            // 清空控制台输入缓冲区，防止 agent 执行期间残留的按键（如 ESC）泄漏到 prompt
            while (Terminal.SupportsKeyAvailable && Console.KeyAvailable)
            {
                Console.ReadKey(intercept: true);
            }

            var userInput = _renderer.PromptUser(SlashCommands);

            // Ctrl+C cancels the prompt and returns null. Exit the chat session rather than
            // treating it as an empty message and immediately prompting again.
            if (userInput is null)
                break;

            if (string.IsNullOrWhiteSpace(userInput))
                continue;

            // MultiLineTextPrompt returns while the caret is still at the end of the submitted
            // input. The first line break closes the user message; the second separates it from
            // the next assistant or system event.
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine();

            if (userInput.Equals(CommandExit, StringComparison.OrdinalIgnoreCase) ||
                userInput.Equals(CommandQuit, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (userInput.Equals(CommandClear, StringComparison.OrdinalIgnoreCase))
            {
                await session.ClearAsync();
                Terminal.Clear();
                _renderer.ShowWarning(CliStrings.ChatContextCleared);
                continue;
            }

            if (userInput.StartsWith("/compact", StringComparison.OrdinalIgnoreCase))
            {
                await HandleCompactCommandAsync(userInput, currentAgent, session);
                continue;
            }

            if (userInput.StartsWith(CommandModel, StringComparison.OrdinalIgnoreCase))
            {
                currentAgent = await HandleModelSwitchAsync(userInput, currentAgent, config, auth, session, summaryService, toolRegistry, skillRegistry, mcpRegistry, userId);
                continue;
            }

            if (session.TryBeginTitleGeneration())
            {
                // Title generation is auxiliary work. It must not delay the next user prompt
                // after the main agent response has already completed.
                _ = GenerateAndSaveSessionTitleAsync(session, summaryService, userInput);
            }

            // 构建上下文（用户消息由 Agent 自动持久化）
            var context = new AgentContext
            {
                SessionId = session.SessionId,
                History = await session.GetLlmHistoryAsync()
            };

            // 执行 Agent（消息持久化由 Agent 通过 IMessageStorage 自动处理）
            await ExecuteAgentAsync(currentAgent, userInput, context);
        }
    }

    private static async Task GenerateAndSaveSessionTitleAsync(
        ChatSession session,
        ISummaryService summaryService,
        string initialUserMessage)
    {
        try
        {
            var title = await summaryService.GenerateTitleAsync(initialUserMessage);

            if (!string.IsNullOrWhiteSpace(title))
                await session.UpdateTitleAsync(title);
        }
        catch
        {
            // Title generation is best-effort and must not affect the chat loop.
        }
    }

    /// <summary>
    /// 处理 /model 命令 - 会话内切换模型
    /// </summary>
    private async Task<Agent> HandleModelSwitchAsync(string input, Agent currentAgent, CliConfig config, AuthConfig auth, ChatSession session, ISummaryService summaryService, ToolRegistry toolRegistry, SkillRegistry skillRegistry, McpRegistry? mcpRegistry, string userId)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            _renderer.ShowWarning(CliStrings.ChatModelUsageHint);
            _renderer.ShowInfo(CliStrings.Format("ChatCurrentModelFormat", config.PrimaryModel));
            if (config.Models.Count > 0)
            {
                _renderer.ShowInfo(CliStrings.ChatAvailableModels);
                foreach (var key in config.Models.Keys)
                {
                    var marker = key == config.PrimaryModel ? CliStrings.ChatCurrentModelMarker : "";
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
            _renderer.ShowError(CliStrings.Format("ChatProviderNotConfiguredFormat", newProviderName));
            return currentAgent;
        }

        // 验证 model 存在
        if (!config.Models.ContainsKey(modelRef))
        {
            _renderer.ShowError(CliStrings.Format("ChatModelNotConfiguredFormat", modelRef));
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
            _renderer.ShowError(CliStrings.Format("ChatLlmClientFailedFormat", ex.Message));
            return currentAgent;
        }

        // 释放旧 agent
        currentAgent.Dispose();

        // 用新模型重建 Agent
        var agentOptions = new AgentCreationOptions
        {
            Config = config,
            Auth = auth,
            LlmClient = newLlmClient,
            Model = newModel,
            ToolRegistry = toolRegistry,
            SkillRegistry = skillRegistry,
            SummaryService = summaryService,
            McpRegistry = mcpRegistry,
            SessionId = session.SessionId,
            UserId = userId
        };
        RegisterDelegationTool(toolRegistry, agentOptions);
        var newAgent = await _agentFactory.CreateAsync(agentOptions);

        _renderer.ShowSuccess(CliStrings.Format("ChatModelSwitchedFormat", newProviderName, newModel.ModelId));
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
            History = await session.GetLlmHistoryAsync()
        };

        _renderer.ShowInfo(CliStrings.Format("ChatCompactingFormat", strategy));

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
                    CliStrings.Format("ChatCompactedFormat", result.StrategyName, result.PreCompactMessages, result.PostCompactMessages, msgDelta, result.PreCompactTokens, result.PostCompactTokens, tokenDelta));
            }
            else
            {
                _renderer.ShowInfo(CliStrings.ChatNothingToCompact);
            }
        }
        catch (Exception ex)
        {
            _renderer.ShowError(CliStrings.Format("ChatCompactFailedFormat", ex.Message));
        }
    }

    private async Task ExecuteAgentAsync(Agent agent, string userInput, AgentContext context)
    {
        using var eventRenderer = new EventRenderer();
        using var cts = new CancellationTokenSource();

        // 启动 ESC 监听后台任务
        // 注意：不能把 cts.Token 传给 Task.Delay，否则取消时会抛异常导致任务异常退出
        var escListenerTask = Terminal.SupportsKeyAvailable
            ? Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Escape)
                        {
                            cts.Cancel();
                            break;
                        }
                    }
                    await Task.Delay(50).ConfigureAwait(false);
                }
            })
            : Task.CompletedTask;

        try
        {
            // 消息持久化由 Agent 自动处理（通过 IMessageStorage）
            // 这里只负责渲染和展示
            await foreach (var agentEvent in agent.RunStreamAsync(userInput, context, cts.Token))
            {
                cts.Token.ThrowIfCancellationRequested();
                await eventRenderer.HandleEventAsync(agentEvent);
            }
        }
        catch (OperationCanceledException)
        {
            await eventRenderer.ShowInterruptedAsync();
        }
        catch (Exception ex)
        {
            _renderer.ShowError(ex.Message);
        }
        finally
        {
            cts.Cancel(); // 确保 ESC 监听任务退出
            try { await escListenerTask; } catch { }
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
            AnsiConsole.MarkupLine(CliStrings.Format("ChatAskUserPromptFormat", Markup.Escape(question)));
            AnsiConsole.WriteLine();

            // 如果没有提供选项，默认使用 Yes/No
            var options = choices is { Length: > 0 } ? choices : [CliStrings.ChatAskUserYes, CliStrings.ChatAskUserNo];


            if (multipleSelect)
            {
                // 多选模式
                var selected = AnsiConsole.Prompt(
                    new MultiSelectionPrompt<string>()
                        .Title($"  {CliStrings.ChatAskUserMultiSelectTitle}")
                        .UseConverter(option => $"  {option}")
                        .NotRequired()
                        .AddChoices(options));

                return selected.Count > 0 ? string.Join(", ", selected) : CliStrings.ChatAskUserNoSelection;
            }
            else
            {
                // 单选模式
                var selected = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title($"  {CliStrings.ChatAskUserSelectTitle}")
                        .UseConverter(option => $"  {option}")
                        .AddChoices(options));

                return selected;
            }
        }));

        return registry;
    }

    private void RegisterDelegationTool(ToolRegistry toolRegistry, AgentCreationOptions template)
    {
        var adapter = new CliInsightaSubagentAdapter(_agentFactory, _storage, template);
        var dispatcher = new SubagentDispatcher([adapter]);
        var catalog = new LocalSubagentCatalog(Directory.GetCurrentDirectory());
        var handler = new CliSubagentDelegationHandler(catalog, dispatcher, template.UserId!);
        toolRegistry.Register(new DelegateTool(handler));
    }

    private static ISummaryService CreateSummaryService(CliConfig config, AuthConfig auth)
    {
        Func<string, ILlmClient> clientFactory = modelRef => LlmClientFactory.Create(auth, config, modelRef);
        return new SummaryService(new SummaryOptions
        {
            Model = config.SecondaryModel ?? config.PrimaryModel,
            ClientFactory = clientFactory
        });
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

    private async Task<ChatSession?> GetOrCreateSessionAsync(string? sessionId, bool continueLast, CliConfig config, string providerName)
    {
        if (!string.IsNullOrEmpty(sessionId))
        {
            var session = await ChatSession.LoadAsync(_storage, sessionId);
            if (session == null)
            {
                _renderer.ShowError(CliStrings.Format("ChatSessionNotFoundFormat", sessionId));
                return null;
            }
            return session;
        }

        if (continueLast)
        {
            var workDir = Directory.GetCurrentDirectory();
            var record = await _storage.GetLastSessionForWorkDirAsync(workDir);
            if (record == null)
            {
                _renderer.ShowError(CliStrings.Format("ChatNoHistoryForWorkDirFormat", workDir));
                return null;
            }
            var session = await ChatSession.LoadAsync(_storage, record.Id);
            if (session == null)
            {
                _renderer.ShowError(CliStrings.Format("ChatSessionCorruptedFormat", record.Id));
                return null;
            }
            _renderer.ShowInfo(CliStrings.Format("ChatSessionResumedFormat", session.SessionId));
            return session;
        }

        var workDir2 = Directory.GetCurrentDirectory();
        return await ChatSession.CreateAsync(_storage, config.PrimaryModel, providerName, workDir2);
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
