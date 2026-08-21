using InsightaAI.Agent;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Cli.Hooks;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Context.Compaction;
using InsightaAI.Agent.Context.Summary;
using InsightaAI.Agent.Diagnostics;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Mcp;
using InsightaAI.Agent.Memory;
using InsightaAI.Agent.MetaLearning;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Mcp.Local;
using InsightaAI.Agent.Skills;
using InsightaAI.Agent.Storage;
using InsightaAI.Agent.Security;
using InsightaAI.LLM.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>
/// 负责创建和配置 CLI 使用的 Agent。
/// </summary>
public sealed class AgentFactory : IAgentFactory
{
    private readonly IMessageStorage _messageStorage;
    private readonly ILoggerFactory _loggerFactory;

    public AgentFactory(IMessageStorage messageStorage, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(messageStorage);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _messageStorage = messageStorage;
        _loggerFactory = loggerFactory;
    }

    public async Task<Agent> CreateAsync(
        AgentCreationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var userId = options.UserId ?? GetOrCreateUserId();
        var defaultAgentConfig = new AgentConfig
        {
            Id = "cli-agent",
            Name = "InsightaAI CLI",
            CustomInstructions = options.Config.CustomInstructions,
            Model = options.Model.ModelId,
            MaxTokens = options.Model.MaxTokens,
            MaxToolRounds = options.Config.MaxToolRounds,
            UserId = userId,
            WorkingDirectory = Directory.GetCurrentDirectory(),
            DenyRules = CreateDenyRules(options.Config.Security),
        };
        var agentConfig = options.AgentConfigOverride is { } profile
            ? ApplyProfile(profile, defaultAgentConfig, options)
            : defaultAgentConfig;

        SessionMemoryHook? sessionMemoryHook = null;
        if (!string.IsNullOrEmpty(options.SessionId))
        {
            sessionMemoryHook = new SessionMemoryHook(
                options.SessionId,
                userId,
                options: new SessionMemoryOptions { EnableLlmSummary = true },
                summaryService: options.SummaryService);
        }

        var contextManager = CreateContextManager(
            options.Model,
            options.SummaryService,
            sessionMemoryHook,
            options.ToolRegistry);

        var mcpRegistry = options.McpRegistry ?? new McpRegistry(new SimpleMcpConnectionPool());
        // Memory storage is an Agent service. Tool exclusions constrain access without
        // removing the service from an isolated subagent's DI container.
        var memoryManager = CreateMemoryManager();
        var environment = new CliEnvironment(options.Config.Envs);
        var agent = new AgentBuilder(agentConfig)
            .WithLlm(options.LlmClient)
            .WithToolRegistry(options.ToolRegistry)
            .WithSkillRegistry(options.SkillRegistry)
            .WithMcpRegistry(mcpRegistry)
            .WithContextManager(contextManager)
            .WithMemoryManager(memoryManager)
            .WithMessageStore(_messageStorage)
            .WithLoggerFactory(_loggerFactory)
            .ConfigureServices(services =>
            {
                services.AddSingleton<IEnvironmentVariableReader>(environment);
            })
            .Build();

        agent.AddHook(new SecurityPolicyHook(agentConfig.DenyRules));
        if (options.EnableInteractiveToolPermission)
        {
            agent.AddHook(new ToolPermissionHook("bash", "write_file", "read_file", "edit_file", "web_fetch"));
        }

        var metaLearningStore = new MetaLearningStore();
        await metaLearningStore.EnsureInitializedAsync();
        agent.AddHook(new MetaLearningHook(metaLearningStore));

        if (sessionMemoryHook != null)
        {
            agent.AddAgentHook(sessionMemoryHook);
        }

        if (environment.Get("INSIGHTA_TELEMETRY") == "1")
        {
            agent.AddTelemetry(options.SessionId);
        }

        return agent;
    }

    private IMemoryManager CreateMemoryManager()
    {
        return new MemoryManager(
            new SqliteMemoryProvider(),
            _loggerFactory.CreateLogger<MemoryManager>());
    }

    private static IContextManager CreateContextManager(
        ModelEntry model,
        ISummaryService summaryService,
        SessionMemoryHook? sessionMemoryHook,
        ToolRegistry toolRegistry)
    {
        var contextWindowTokens = model.ContextWindow > 0
            ? model.ContextWindow.Value
            : ModelContextWindows.GetContextWindowSize(model.ModelId);

        var budget = new ContextBudget
        {
            MaxContextTokens = contextWindowTokens,
            ReservedForOutput = model.MaxTokens ?? 16_384,
            Enabled = true
        };

        var strategies = new List<ICompactStrategy>
        {
            new MicroCompactStrategy(toolRegistry)
        };

        if (sessionMemoryHook != null)
        {
            strategies.Add(new SessionMemoryCompactStrategy(sessionMemoryHook));
        }

        strategies.Add(new TraditionalCompactStrategy(summaryService));
        return new ContextManager(new CharTokenEstimator(), budget, strategies);
    }

    internal static string GetOrCreateUserId()
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

        var userId = $"user_{Guid.NewGuid().ToString("N")[..12]}";
        File.WriteAllText(userIdFile, userId);
        return userId;
    }

    private static IReadOnlyList<DenyRule> CreateDenyRules(SecurityConfig security)
    {
        ArgumentNullException.ThrowIfNull(security);

        return security.DenyList.Select(entry =>
        {
            if (string.IsNullOrWhiteSpace(entry.Pattern))
                throw new InvalidOperationException("security.deny_list entries require a non-empty pattern.");

            if (!Enum.TryParse<DenyMatchMode>(entry.Mode, ignoreCase: true, out var mode))
                throw new InvalidOperationException($"Unsupported security.deny_list mode '{entry.Mode}'. Use exact, glob, or regex.");

            if (mode == DenyMatchMode.Regex)
            {
                try
                {
                    _ = new Regex(entry.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidOperationException(
                        $"Invalid security.deny_list regex '{entry.Pattern}'.", exception);
                }
            }

            return new DenyRule(entry.Pattern, mode);
        }).ToArray();
    }

    private static AgentConfig ApplyProfile(
        AgentConfig profile,
        AgentConfig cliDefaults,
        AgentCreationOptions options)
    {
        if (!string.Equals(profile.Model, options.Model.ModelId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Invocation AgentConfig model '{profile.Model}' does not match resolved CLI model '{options.Model.ModelId}'.");
        }

        return profile with
        {
            MaxTokens = profile.MaxTokens is { } profileMaxTokens && cliDefaults.MaxTokens is { } cliMaxTokens
                ? Math.Min(profileMaxTokens, cliMaxTokens)
                : profile.MaxTokens ?? cliDefaults.MaxTokens,
            MaxToolRounds = Math.Min(profile.MaxToolRounds, cliDefaults.MaxToolRounds),
            UserId = cliDefaults.UserId,
            WorkingDirectory = cliDefaults.WorkingDirectory,
            DenyRules = cliDefaults.DenyRules
        };
    }
}
