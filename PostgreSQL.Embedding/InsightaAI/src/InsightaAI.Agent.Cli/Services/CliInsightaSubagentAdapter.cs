using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Storage;
using InsightaAI.Agents.Subagents.Definitions;
using InsightaAI.Agents.Subagents.Invocation;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using System.Text;

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
        var model = ResolveModel(definition);
        var profile = CreateProfile(definition, model);
        var tools = CreateRestrictedToolRegistry(definition, request.AllowedToolNames);
        var session = await GetOrCreateSessionAsync(request, model, cancellationToken);
        var history = (await _storage.GetMessagesAsync(session.Id))
            .Select(record => record.ToLlmMessage())
            .ToList();
        var options = _template with
        {
            LlmClient = model.Client,
            Model = model.Entry,
            ToolRegistry = tools,
            SessionId = session.Id,
            UserId = request.Context.UserId,
            EnableInteractiveToolPermission = false,
            AgentConfigOverride = profile
        };

        using var agent = await _agentFactory.CreateAsync(options, cancellationToken);
        await ReportProgressAsync(request.Progress, new SubagentProgressUpdate
        {
            Kind = SubagentProgressKind.Started,
            Message = $"{definition.Name} started."
        }, cancellationToken);

        AgentResult? result = null;
        var streamedOutput = new StringBuilder();
        await foreach (var agentEvent in agent.RunStreamAsync(request.Input, new AgentContext
        {
            SessionId = session.Id,
            History = history
        }, cancellationToken))
        {
            switch (agentEvent)
            {
                case AgentLlmStreamEvent { StreamEvent: TextDeltaEvent { Delta: { Length: > 0 } text } }:
                    streamedOutput.Append(text);
                    await ReportProgressAsync(request.Progress, new SubagentProgressUpdate
                    {
                        Kind = SubagentProgressKind.Output,
                        Text = text
                    }, cancellationToken);
                    break;

                case AgentRoundStartEvent round:
                    await ReportProgressAsync(request.Progress, new SubagentProgressUpdate
                    {
                        Kind = SubagentProgressKind.Status,
                        Message = $"{definition.Name} started round {round.Round}.",
                        Round = round.Round
                    }, cancellationToken);
                    break;

                case AgentToolStartEvent tool:
                    await ReportProgressAsync(request.Progress, new SubagentProgressUpdate
                    {
                        Kind = SubagentProgressKind.Status,
                        Message = $"{definition.Name} is using {tool.ToolName}.",
                        ToolName = tool.ToolName
                    }, cancellationToken);
                    break;

                case AgentToolProgressEvent toolProgress:
                    var progress = toolProgress.Progress;
                    await ReportProgressAsync(request.Progress, new SubagentProgressUpdate
                    {
                        Kind = progress.Kind == ToolProgressKind.Output
                            ? SubagentProgressKind.Output
                            : SubagentProgressKind.Status,
                        Message = progress.Message,
                        Text = progress.Text,
                        Stream = progress.Stream switch
                        {
                            ToolOutputStream.Stdout => SubagentOutputStream.Stdout,
                            ToolOutputStream.Stderr => SubagentOutputStream.Stderr,
                            _ => null
                        }
                    }, cancellationToken);
                    break;

                case AgentTurnEndEvent turnEnd:
                    result = turnEnd.Result;
                    break;
            }
        }

        result ??= new AgentResult { Status = AgentStatus.Failed, Error = "Subagent did not produce a completion event." };
        var output = result.Message?.Content.OfType<TextBlock>().FirstOrDefault()?.Text;
        if (string.IsNullOrEmpty(output))
            output = streamedOutput.ToString();

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

    private static async ValueTask ReportProgressAsync(
        ISubagentProgressReporter? reporter,
        SubagentProgressUpdate update,
        CancellationToken cancellationToken)
    {
        if (reporter is null)
            return;

        try
        {
            await reporter.ReportAsync(update, cancellationToken);
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            // Progress is an optional observer path and must not fail the invocation.
        }
    }

    private async Task<SessionRecord> GetOrCreateSessionAsync(
        SubagentInvocationRequest request,
        ResolvedSubagentModel model,
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

        var provider = Cli.Models.CliConfig.ParseModelReference(model.Reference).ProviderName;
        return await _storage.CreateSessionAsync(
            model.Entry.ModelId,
            provider,
            userId: request.Context.UserId,
            parentSessionId: request.Context.ParentSessionId,
            parentInvocationId: request.Context.ParentInvocationId,
            invocationId: request.Context.InvocationId);
    }

    private ResolvedSubagentModel ResolveModel(InsightaSubagentDefinition definition)
    {
        // A missing descriptor value deliberately means the configured primary model, not the
        // potentially session-switched model held by the parent AgentCreationOptions template.
        var modelReference = definition.Model ?? _template.Config.PrimaryModel;
        CliConfig.ParseModelReference(modelReference);
        var model = _template.Config.GetModel(modelReference);
        var client = LlmClientFactory.Create(_template.Auth, _template.Config, modelReference);
        return new ResolvedSubagentModel(modelReference, model, client);
    }

    private AgentConfig CreateProfile(InsightaSubagentDefinition definition, ResolvedSubagentModel model)
    {
        var excludedToolNames = CreateExcludedToolNames(definition.Capabilities);
        return new AgentConfig
        {
            Id = definition.Id,
            Name = definition.Name,
            Model = model.Entry.ModelId,
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
        var permittedNames = (definition.ToolNames ?? []).AsEnumerable();
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

    private sealed record ResolvedSubagentModel(string Reference, ModelEntry Entry, ILlmClient Client);
}
