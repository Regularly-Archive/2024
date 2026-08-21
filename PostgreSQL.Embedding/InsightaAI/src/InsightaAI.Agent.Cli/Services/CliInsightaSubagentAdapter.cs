using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Storage;
using InsightaAI.Agents.Subagents.Definitions;
using InsightaAI.Agents.Subagents.Invocation;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>
/// CLI host adapter for Insighta-runtime subagents. It owns session/profile/tool preparation;
/// the shared Subagents package intentionally knows none of those CLI or Agent details.
/// </summary>
public sealed class CliInsightaSubagentAdapter : ISubagentAdapter
{
    private static readonly string[] SkillToolNames = ["activate_skill", "list_skills"];
    private static readonly string[] McpToolNames = ["list_mcp_tools", "activate_mcp_tool", "deactivate_mcp_tool"];
    private static readonly string[] MemoryToolNames =
        ["save_memory", "update_memory", "delete_memory", "search_memory", "get_user_profile"];

    private readonly IAgentFactory _agentFactory;
    private readonly IMessageStorage _storage;
    private readonly AgentCreationOptions _template;

    public CliInsightaSubagentAdapter(
        IAgentFactory agentFactory,
        IMessageStorage storage,
        AgentCreationOptions template)
    {
        ArgumentNullException.ThrowIfNull(agentFactory);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(template);
        _agentFactory = agentFactory;
        _storage = storage;
        _template = template;
    }

    public bool CanInvoke(SubagentDefinition definition) => definition is InsightaSubagentDefinition;

    public async Task<SubagentInvocationResult> InvokeAsync(
        SubagentInvocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Definition is not InsightaSubagentDefinition definition)
            throw new InvalidOperationException("The CLI Insighta adapter requires an Insighta subagent definition.");
        if (string.IsNullOrWhiteSpace(request.Context.UserId))
            throw new InvalidOperationException("CLI subagent invocations require a host-validated user ID.");
        if (string.IsNullOrWhiteSpace(request.Context.InvocationId))
            throw new InvalidOperationException("CLI subagent invocations require a host-generated invocation ID.");

        cancellationToken.ThrowIfCancellationRequested();
        var profile = CreateProfile(definition);
        var tools = CreateRestrictedToolRegistry(definition, request.AllowedToolNames);
        var session = await GetOrCreateSessionAsync(request, cancellationToken);
        var history = (await _storage.GetMessagesAsync(session.Id))
            .Select(record => record.ToLlmMessage())
            .ToList();
        var options = _template with
        {
            ToolRegistry = tools,
            SessionId = session.Id,
            UserId = request.Context.UserId,
            EnableInteractiveToolPermission = false,
            AgentConfigOverride = profile
        };

        using var agent = await _agentFactory.CreateAsync(options, cancellationToken);
        var result = await agent.RunAsync(request.Input, new AgentContext
        {
            SessionId = session.Id,
            History = history
        }, cancellationToken);
        var output = result.Message?.Content.OfType<TextBlock>().FirstOrDefault()?.Text;

        return new SubagentInvocationResult
        {
            InvocationId = request.Context.InvocationId,
            SessionId = session.Id,
            Status = result.Status switch
            {
                AgentStatus.Completed => SubagentInvocationStatus.Completed,
                _ => SubagentInvocationStatus.Failed
            },
            Output = output,
            Error = result.Error
        };
    }

    private async Task<SessionRecord> GetOrCreateSessionAsync(
        SubagentInvocationRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Context.SessionId))
        {
            if (string.Equals(request.Context.SessionId, request.Context.ParentSessionId, StringComparison.Ordinal))
                throw new InvalidOperationException("A parent session cannot be reused as a child subagent session.");

            var existing = await _storage.GetSessionAsync(request.Context.SessionId);
            if (existing == null)
                throw new InvalidOperationException($"Child session '{request.Context.SessionId}' was not found.");
            if (!string.Equals(existing.UserId, request.Context.UserId, StringComparison.Ordinal))
                throw new InvalidOperationException("Child session does not belong to the invocation user.");
            if (string.IsNullOrWhiteSpace(existing.ParentSessionId) ||
                string.IsNullOrWhiteSpace(existing.ParentInvocationId) ||
                string.IsNullOrWhiteSpace(existing.InvocationId))
            {
                throw new InvalidOperationException("Only an existing child subagent session can be reused.");
            }
            if (!string.Equals(existing.ParentSessionId, request.Context.ParentSessionId, StringComparison.Ordinal) ||
                !string.Equals(existing.ParentInvocationId, request.Context.ParentInvocationId, StringComparison.Ordinal) ||
                !string.Equals(existing.InvocationId, request.Context.InvocationId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Child session linkage does not match this subagent invocation.");
            }
            return existing;
        }

        var provider = Cli.Models.CliConfig.ParseModelReference(_template.Config.PrimaryModel).ProviderName;
        return await _storage.CreateSessionAsync(
            _template.Model.ModelId,
            provider,
            userId: request.Context.UserId,
            parentSessionId: request.Context.ParentSessionId,
            parentInvocationId: request.Context.ParentInvocationId,
            invocationId: request.Context.InvocationId);
    }

    private AgentConfig CreateProfile(InsightaSubagentDefinition definition)
    {
        var excludedToolNames = CreateExcludedToolNames(definition.Capabilities);
        return new AgentConfig
        {
            Id = definition.Id,
            Name = definition.Name,
            Model = definition.Model ?? _template.Model.ModelId,
            CustomInstructions = AppendRuntimeConstraints(definition.Instructions, excludedToolNames),
            MaxTokens = definition.MaxTokens,
            MaxToolRounds = definition.MaxToolRounds ?? 15,
            IncludeProjectInstructions = definition.IncludeProjectInstructions,
            ExcludedToolNames = excludedToolNames
        };
    }

    private IReadOnlyList<string> CreateExcludedToolNames(InsightaSubagentCapabilities requested)
    {
        var excluded = new List<string> { "delegate" };
        AddGroupWhenUnavailable(excluded, requested.EnableSkills, SkillToolNames);
        AddGroupWhenUnavailable(excluded, requested.EnableMcp, McpToolNames);
        AddGroupWhenUnavailable(excluded, requested.EnableMemory, MemoryToolNames);
        return excluded;
    }

    private void AddGroupWhenUnavailable(List<string> excluded, bool requested, IReadOnlyList<string> toolNames)
    {
        if (requested && toolNames.All(_template.ToolRegistry.HasTool))
            return;

        excluded.AddRange(toolNames);
    }

    private static string AppendRuntimeConstraints(string instructions, IReadOnlyList<string> excludedToolNames)
    {
        var constraints = new List<string>
        {
            "This invocation cannot delegate work to another agent."
        };

        if (IsGroupExcluded(excludedToolNames, SkillToolNames))
            constraints.Add("Skill tools are unavailable. Do not attempt to list or activate skills.");
        if (IsGroupExcluded(excludedToolNames, McpToolNames))
            constraints.Add("MCP management tools are unavailable. Do not attempt to discover or activate MCP tools.");
        if (IsGroupExcluded(excludedToolNames, MemoryToolNames))
            constraints.Add("Memory tools are unavailable. You may receive memory context, but do not attempt to search or change memories.");

        constraints.Add("Use only the tools exposed in this invocation.");
        var runtimeSection = "### Runtime constraints\n" + string.Join("\n", constraints.Select(constraint => $"- {constraint}"));
        return string.IsNullOrWhiteSpace(instructions)
            ? runtimeSection
            : $"{instructions.TrimEnd()}\n\n{runtimeSection}";
    }

    private static bool IsGroupExcluded(IReadOnlyList<string> excludedToolNames, IReadOnlyList<string> group)
    {
        return group.All(toolName => excludedToolNames.Contains(toolName, StringComparer.Ordinal));
    }

    private ToolRegistry CreateRestrictedToolRegistry(
        InsightaSubagentDefinition definition,
        IReadOnlyList<string>? requestToolNames)
    {
        var permittedNames = definition.ToolNames.AsEnumerable();
        if (requestToolNames is not null)
            permittedNames = permittedNames.Intersect(requestToolNames, StringComparer.Ordinal);

        var registry = new ToolRegistry();
        foreach (var toolName in permittedNames.Distinct(StringComparer.Ordinal))
        {
            var tool = _template.ToolRegistry.GetExecutor(toolName)
                ?? throw new InvalidOperationException(
                    $"Subagent definition '{definition.Id}' requests tool '{toolName}', but the CLI host has not registered it.");
            registry.Register(tool);
        }
        return registry;
    }
}
