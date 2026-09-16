using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Cli.Hooks;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Cli.Services;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Context.Summary;
using InsightaAI.Agent.Diagnostics;
using InsightaAI.Agent.Extensions;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Mcp;
using InsightaAI.Agent.Mcp.Local;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Skills;
using InsightaAI.Agent.Storage;
using InsightaAI.Agent.Tools;
using InsightaAI.Agents.Subagents.Definitions;
using InsightaAI.LLM.Abstractions;
using Microsoft.Extensions.Logging;

namespace InsightaAI.Agent.Cli.Run;

/// <summary>
/// Executes one non-interactive Agent turn while preserving the real Agent runtime and the
/// existing <see cref="AgentEvent"/> stream. The JSONL writer is only a transport boundary.
/// </summary>
public sealed class RunApplication
{
    private static readonly string[] SkillToolNames = ["activate_skill", "list_skills"];
    private static readonly string[] McpToolNames = ["list_mcp_tools", "activate_mcp_tool", "deactivate_mcp_tool"];
    private static readonly string[] MemoryToolNames =
        ["save_memory", "update_memory", "delete_memory", "search_memory", "get_user_profile"];
    private static readonly string[] NonInteractiveDefaultDeniedTools =
    [
        "ask_user", "delegate", "bash", "write_file", "edit_file", "web_fetch", "web_search",
        .. SkillToolNames,
        .. McpToolNames,
        .. MemoryToolNames
    ];

    private readonly IMessageStorage _storage;
    private readonly IAgentFactory _agentFactory;
    private readonly CliConfig _config;
    private readonly AuthConfig _auth;
    private readonly Func<AuthConfig, string?, ILlmClient> _clientFactory;
    private readonly Func<string, CancellationToken, ValueTask<SubagentDefinition?>> _profileResolver;

    public RunApplication(
        IMessageStorage storage,
        IAgentFactory agentFactory,
        CliConfig config,
        AuthConfig? auth = null,
        Func<AuthConfig, string?, ILlmClient>? clientFactory = null,
        Func<string, CancellationToken, ValueTask<SubagentDefinition?>>? profileResolver = null)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(agentFactory);
        ArgumentNullException.ThrowIfNull(config);
        _storage = storage;
        _agentFactory = agentFactory;
        _config = config;
        _auth = auth ?? AuthConfig.Load();
        _clientFactory = clientFactory ?? ((credentials, modelReference) => modelReference is null
            ? LlmClientFactory.Create(credentials, _config)
            : LlmClientFactory.Create(credentials, _config, modelReference));
        _profileResolver = profileResolver ?? ((profileId, cancellationToken) =>
            new LocalSubagentDefinitionStore().FindAsync(profileId, cancellationToken));
    }

    public async Task<RunExecutionResult> ExecuteAsync(
        RunRequest request,
        TextWriter stdout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stdout);

        var writer = new RunJsonlWriter(stdout);
        Agent? agent = null;
        ILlmClient? client = null;
        string? sessionId = null;
        AgentResult? result = null;

        try
        {
            ValidateRequest(request);
            ValidateConfig();
            cancellationToken.ThrowIfCancellationRequested();

            AgentCreationOptions options;
            if (!string.IsNullOrWhiteSpace(request.ProfileId))
            {
                var resolved = await ResolveProfileAsync(request, _auth, cancellationToken).ConfigureAwait(false);
                client = resolved.Client;
                options = resolved.Options;
            }
            else
            {
                client = _clientFactory(_auth, null);
                var toolRegistry = CreateDefaultRunToolRegistry();
                options = new AgentCreationOptions
                {
                    Config = _config,
                    Auth = _auth,
                    LlmClient = client,
                    Model = _config.GetModel(_config.PrimaryModel),
                    ToolRegistry = toolRegistry,
                    SkillRegistry = CreateSkillRegistry(),
                    SummaryService = CreateSummaryService(_auth),
                    McpRegistry = CreateMcpRegistry(),
                    EnableInteractiveToolPermission = false
                };
            }

            var session = await GetSessionAsync(request.SessionId, options.Model, cancellationToken).ConfigureAwait(false);
            sessionId = session.SessionId;
            options = options with { SessionId = session.SessionId };

            agent = await _agentFactory.CreateAsync(options, cancellationToken).ConfigureAwait(false);
            await writer.RunStartedAsync(
                session.SessionId,
                options.Model.ModelId,
                request.ProfileId,
                cancellationToken).ConfigureAwait(false);

            var context = new AgentContext
            {
                SessionId = session.SessionId,
                History = await session.GetLlmHistoryAsync().ConfigureAwait(false)
            };

            await foreach (var agentEvent in agent.RunStreamAsync(request.Input, context, cancellationToken)
                .ConfigureAwait(false))
            {
                await writer.AgentEventAsync(agentEvent, cancellationToken).ConfigureAwait(false);
                if (agentEvent is AgentTurnEndEvent ended)
                    result = ended.Result;
            }

            var exitCode = result?.Status == AgentStatus.Completed
                ? ExitCodes.Success
                : result?.Status == AgentStatus.Aborted
                    ? ExitCodes.Cancelled
                    : ExitCodes.Failed;

            await writer.RunCompletedAsync(sessionId, result, exitCode, cancellationToken).ConfigureAwait(false);
            return new RunExecutionResult
            {
                ExitCode = exitCode,
                SessionId = sessionId,
                Status = result?.Status
            };
        }
        catch (OperationCanceledException)
        {
            await writer.RunCompletedAsync(sessionId, result, ExitCodes.Cancelled, CancellationToken.None)
                .ConfigureAwait(false);
            return new RunExecutionResult
            {
                ExitCode = ExitCodes.Cancelled,
                SessionId = sessionId,
                Status = result?.Status ?? AgentStatus.Aborted
            };
        }
        catch (Exception exception)
        {
            await writer.RunFailedAsync(sessionId, exception.Message, ExitCodes.Failed, CancellationToken.None)
                .ConfigureAwait(false);
            return new RunExecutionResult
            {
                ExitCode = ExitCodes.Failed,
                SessionId = sessionId
            };
        }
        finally
        {
            agent?.Dispose();
            client?.Dispose();
        }
    }

    private async Task<ResolvedProfile> ResolveProfileAsync(
        RunRequest request,
        AuthConfig auth,
        CancellationToken cancellationToken)
    {
        var definition = await _profileResolver(request.ProfileId!, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Subagent profile '{request.ProfileId}' was not found.");
        if (definition is not InsightaSubagentDefinition insightaDefinition)
            throw new InvalidOperationException(
                $"Run profile '{request.ProfileId}' is not an Insighta subagent definition.");

        var modelReference = insightaDefinition.Model ?? _config.PrimaryModel;
        var model = _config.GetModel(modelReference);
        var client = _clientFactory(auth, modelReference);

        var hostTools = CreateHostToolRegistry();
        var toolRegistry = CreateProfileToolRegistry(
            hostTools,
            insightaDefinition,
            request.AllowedToolNames);

        var options = new AgentCreationOptions
        {
            Config = _config,
            Auth = auth,
            LlmClient = client,
            Model = model,
            ToolRegistry = toolRegistry,
            SkillRegistry = CreateSkillRegistry(),
            SummaryService = CreateSummaryService(auth, modelReference),
            McpRegistry = CreateMcpRegistry(),
            EnableInteractiveToolPermission = false,
            AgentConfigOverride = CreateProfile(insightaDefinition, model)
        };

        return new ResolvedProfile(client, options);
    }

    private async Task<ChatSession> GetSessionAsync(
        string? sessionId,
        ModelEntry model,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var existing = await ChatSession.LoadAsync(_storage, sessionId).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");
            if (!string.Equals(existing.Model, model.ModelId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Session '{sessionId}' uses model '{existing.Model}', but run profile resolved '{model.ModelId}'.");
            }
            return existing;
        }

        var provider = CliConfig.ParseModelReference(
            GetModelReference(model.ModelId)).ProviderName;
        var record = await _storage.CreateSessionAsync(
            model.ModelId,
            provider,
            userId: AgentFactory.GetOrCreateUserId(),
            workDir: Directory.GetCurrentDirectory()).ConfigureAwait(false);
        return new ChatSession(_storage, record);
    }

    private string GetModelReference(string modelId)
    {
        if (!string.IsNullOrWhiteSpace(_config.PrimaryModel) &&
            string.Equals(_config.GetModel(_config.PrimaryModel).ModelId, modelId, StringComparison.Ordinal))
        {
            return _config.PrimaryModel;
        }

        return _config.Models.FirstOrDefault(pair =>
            string.Equals(pair.Value.ModelId, modelId, StringComparison.Ordinal)).Key
            ?? throw new InvalidOperationException($"Model '{modelId}' is not configured.");
    }

    private ToolRegistry CreateDefaultRunToolRegistry()
    {
        var registry = new ToolRegistry();
        if (_config.EnableBuiltInTools)
            registry.AddBuiltInTools();

        return registry.Exclude(NonInteractiveDefaultDeniedTools);
    }

    private ToolRegistry CreateHostToolRegistry()
    {
        var registry = new ToolRegistry();
        if (_config.EnableBuiltInTools)
            registry.AddBuiltInTools();

        return registry.Exclude(["ask_user", "delegate"]);
    }

    private static ToolRegistry CreateProfileToolRegistry(
        ToolRegistry hostTools,
        InsightaSubagentDefinition definition,
        IReadOnlyList<string>? allowedToolNames)
    {
        var permitted = definition.ToolNames.AsEnumerable();
        if (allowedToolNames is not null)
        {
            var allowed = allowedToolNames.Distinct(StringComparer.Ordinal).ToArray();
            var unknown = allowed.Except(definition.ToolNames, StringComparer.Ordinal).ToArray();
            if (unknown.Length > 0)
            {
                throw new InvalidOperationException(
                    $"--allowed-tools includes {string.Join(", ", unknown.Select(name => $"'{name}'"))}, " +
                    $"but profile '{definition.Id}' does not allow it.");
            }

            permitted = permitted.Where(allowed.Contains);
        }

        var registry = new ToolRegistry();
        foreach (var toolName in permitted.Distinct(StringComparer.Ordinal))
        {
            var tool = hostTools.GetExecutor(toolName)
                ?? throw new InvalidOperationException(
                    $"Subagent profile '{definition.Id}' requests unavailable tool '{toolName}'.");
            registry.Register(tool);
        }

        return registry;
    }

    private static AgentConfig CreateProfile(InsightaSubagentDefinition definition, ModelEntry model)
    {
        var excludedToolNames = CreateExcludedToolNames(definition.Capabilities);
        return new AgentConfig
        {
            Id = definition.Id,
            Name = definition.Name,
            Model = model.ModelId,
            CustomInstructions = AppendRuntimeConstraints(definition.Instructions, excludedToolNames),
            MaxTokens = definition.MaxTokens,
            MaxToolRounds = definition.MaxToolRounds ?? 15,
            IncludeProjectInstructions = definition.IncludeProjectInstructions,
            ExcludedToolNames = excludedToolNames
        };
    }

    private static IReadOnlyList<string> CreateExcludedToolNames(InsightaSubagentCapabilities requested)
    {
        var excluded = new List<string> { "delegate" };
        AddGroupWhenUnavailable(excluded, requested.EnableSkills, SkillToolNames);
        AddGroupWhenUnavailable(excluded, requested.EnableMcp, McpToolNames);
        AddGroupWhenUnavailable(excluded, requested.EnableMemory, MemoryToolNames);
        return excluded;
    }

    private static void AddGroupWhenUnavailable(
        List<string> excluded,
        bool requested,
        IReadOnlyList<string> toolNames)
    {
        if (!requested)
            excluded.AddRange(toolNames);
    }

    private static string AppendRuntimeConstraints(
        string instructions,
        IReadOnlyList<string> excludedToolNames)
    {
        var constraints = new List<string>
        {
            "This invocation cannot delegate work to another agent.",
            "This invocation is non-interactive; no tool approval prompt is available."
        };
        if (IsGroupExcluded(excludedToolNames, SkillToolNames))
            constraints.Add("Skill tools are unavailable. Do not attempt to list or activate skills.");
        if (IsGroupExcluded(excludedToolNames, McpToolNames))
            constraints.Add("MCP tools are unavailable. Do not attempt to discover or activate MCP tools.");
        if (IsGroupExcluded(excludedToolNames, MemoryToolNames))
            constraints.Add("Memory tools are unavailable. Do not attempt to search or change memories.");
        constraints.Add("Use only the tools exposed in this invocation.");

        var section = "### Runtime constraints\n" +
            string.Join("\n", constraints.Select(constraint => $"- {constraint}"));
        return string.IsNullOrWhiteSpace(instructions)
            ? section
            : $"{instructions.TrimEnd()}\n\n{section}";
    }

    private static bool IsGroupExcluded(
        IReadOnlyList<string> excludedToolNames,
        IReadOnlyList<string> group)
    {
        return group.All(excludedToolNames.Contains);
    }

    private static SkillRegistry CreateSkillRegistry()
    {
        var registry = new SkillRegistry();
        if (Directory.Exists(CliConfig.GlobalSkillsDir))
            registry.RegisterProvider(new InsightaAI.Agent.Skills.Local.LocalSkillProvider(CliConfig.GlobalSkillsDir));
        if (Directory.Exists(CliConfig.ProjectSkillsDir))
            registry.RegisterProvider(new InsightaAI.Agent.Skills.Local.LocalSkillProvider(CliConfig.ProjectSkillsDir));
        return registry;
    }

    private static McpRegistry? CreateMcpRegistry()
    {
        var globalPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".agents", "mcp-servers.json");
        var projectPath = Path.Combine(Directory.GetCurrentDirectory(), ".insighta", "mcp-servers.json");
        if (!File.Exists(globalPath) && !File.Exists(projectPath))
            return null;

        var registry = new McpRegistry(new SimpleMcpConnectionPool());
        if (File.Exists(globalPath))
            registry.RegisterProvider(new InsightaAI.Agent.Mcp.Local.JsonMcpServerProvider(globalPath));
        if (File.Exists(projectPath))
            registry.RegisterProvider(new InsightaAI.Agent.Mcp.Local.JsonMcpServerProvider(projectPath));
        return registry;
    }

    private ISummaryService CreateSummaryService(AuthConfig auth, string? modelReference = null)
    {
        var model = modelReference ?? _config.SecondaryModel ?? _config.PrimaryModel;
        return new SummaryService(new SummaryOptions
        {
            Model = model,
            ClientFactory = reference => _clientFactory(auth, reference)
        });
    }

    private void ValidateConfig()
    {
        if (string.IsNullOrWhiteSpace(_config.PrimaryModel))
            throw new InvalidOperationException("Primary model is not configured.");

        var (providerName, _) = _config.ParsePrimaryModel();
        if (!_auth.Providers.ContainsKey(providerName))
            throw new InvalidOperationException($"Provider '{providerName}' is not configured in auth.json.");
        if (!_config.Models.ContainsKey(_config.PrimaryModel))
            throw new InvalidOperationException($"Model '{_config.PrimaryModel}' is not configured.");
    }

    private static void ValidateRequest(RunRequest request)
    {
        if (request.AllowedToolNames is not null && string.IsNullOrWhiteSpace(request.ProfileId))
        {
            throw new InvalidOperationException("--allowed-tools requires --profile.");
        }

        if (request.AllowedToolNames is { Count: 0 })
            throw new InvalidOperationException("--allowed-tools requires at least one tool name.");
    }

    private sealed record ResolvedProfile(ILlmClient Client, AgentCreationOptions Options);
}
