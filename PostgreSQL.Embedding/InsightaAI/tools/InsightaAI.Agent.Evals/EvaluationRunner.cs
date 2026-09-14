using System.Text.Json;
using InsightaAI.Agent;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Context.Compaction;
using InsightaAI.Agent.Harness.Local;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Security;
using InsightaAI.Agent.Storage;
using InsightaAI.Agent.Tools;
using InsightaAI.Agent.Tools.BuiltIn;
using InsightaAI.LLM.Models;
using Microsoft.Extensions.DependencyInjection;

namespace InsightaAI.Agent.Evals;

public sealed class EvaluationRunner
{
    public async Task<EvaluationReport> RunAsync(EvaluationScenario scenario, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario.Prompt);
        if (scenario.Steps.Count == 0)
            throw new InvalidOperationException($"Scenario '{scenario.Id}' must contain at least one recorded LLM step.");

        var startedAt = DateTimeOffset.UtcNow;
        var workspace = PrepareWorkspace(scenario);
        try
        {
            var registry = CreateToolRegistry(scenario, workspace.FixtureDirectory);
            using var client = new RecordedLlmClient(ExpandFixturePlaceholders(scenario.Steps, workspace.FixtureDirectory));
            IMessageStorage? storage = scenario.UseMessageStorage
                ? new JsonlMessageStorage(workspace.StorageDirectory)
                : null;
            var config = new AgentConfig
            {
                Id = $"eval-{scenario.Id}",
                Name = $"Evaluation: {scenario.Id}",
                Model = "recorded-model",
                MaxToolRounds = scenario.Steps.Count,
                ParallelToolExecution = false,
                IncludeProjectInstructions = false,
                DenyRules = ParseDenyRules(scenario)
            };
            using var agent = new AgentBuilder(config)
                .WithLlm(client)
                .WithToolRegistry(registry)
                .ConfigureServices(services =>
                {
                    if (scenario.MaxContextTokens is { } maxTokens)
                    {
                        var budget = new ContextBudget
                        {
                            MaxContextTokens = maxTokens,
                            ReservedForOutput = 1000,
                            KeepRecentToolResults = 1
                        };
                        services.AddSingleton<IContextManager>(
                            new ContextManager(new CharTokenEstimator(), budget,
                                [new MicroCompactStrategy(registry)]));
                    }
                })
                .ConfigureServices(services =>
                {
                    if (storage != null)
                        services.AddSingleton(storage);
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IToolResultArtifactStore>(
                        new ToolResultArtifactStore(new LocalFileSystem(), workspace.ArtifactDirectory));
                })
                .Build();
            foreach (var toolName in scenario.AlwaysAllowTools)
                agent.AddHook(new AlwaysAllowHook(toolName));
            if (config.DenyRules.Count > 0)
                agent.AddHook(new SecurityPolicyHook(config.DenyRules));

            var sessionId = $"eval-{scenario.Id}";
            if (storage != null)
                Directory.CreateDirectory(Path.Combine(workspace.StorageDirectory, sessionId));

            var events = new List<AgentEvent>();
            await foreach (var agentEvent in agent.RunStreamAsync(
                scenario.Prompt,
                new AgentContext { SessionId = sessionId },
                cancellationToken))
            {
                events.Add(agentEvent);
            }

            var completed = events.OfType<AgentTurnEndEvent>().LastOrDefault();
            var toolStarts = events.OfType<AgentToolStartEvent>().ToArray();
            var toolEnds = events.OfType<AgentToolEndEvent>().ToArray();
            var compactedEvents = events.OfType<AgentContextCompactedEvent>().ToArray();
            var failures = Verify(scenario, completed, toolStarts, toolEnds, events, workspace.FixtureDirectory, storage, compactedEvents);
            var duration = completed?.Result.DurationMs ?? (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

            return new EvaluationReport
            {
                ScenarioId = scenario.Id,
                Passed = failures.Count == 0,
                StartedAt = startedAt,
                DurationMs = duration,
                Failures = failures,
                Metrics = new EvaluationMetrics
                {
                    LlmRequests = client.RequestCount,
                    Turns = events.Count(x => x is AgentTurnStartEvent),
                    Rounds = events.Count(x => x is AgentRoundStartEvent),
                    ToolCalls = toolStarts.Length,
                    ToolErrors = toolEnds.Count(x => x.IsError),
                    AgentErrors = events.Count(x => x is AgentErrorEvent)
                }
            };
        }
        finally
        {
            if (Directory.Exists(workspace.RootDirectory))
                Directory.Delete(workspace.RootDirectory, recursive: true);
        }
    }

    private static ToolRegistry CreateToolRegistry(EvaluationScenario scenario, string? fixtureDirectory)
    {
        var registry = new ToolRegistry();
        if (fixtureDirectory != null)
        {
            var fileSystem = new LocalFileSystem();
            var readState = new FileReadState();
            registry.Register(new FileReadTool(fileSystem, readState));
            registry.Register(new FileEditTool(fileSystem, new LocalPathValidator(), readState));
        }

        if (scenario.DelegateResult != null)
            registry.Register(new DelegateTool(new StubDelegationHandler(scenario.DelegateResult)));

        foreach (var tool in scenario.Tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name))
                throw new InvalidOperationException($"Scenario '{scenario.Id}' contains a tool without a name.");
            if (tool.Repeat <= 0)
                throw new InvalidOperationException($"Fixture tool '{tool.Name}' must have a positive repeat count.");

            var result = string.Concat(Enumerable.Repeat(tool.Result, tool.Repeat));

            registry.RegisterFunction(
                tool.Name,
                $"Evaluation fixture tool '{tool.Name}'.",
                JsonSerializer.SerializeToElement(new { type = "object" }),
                (_, _) => Task.FromResult(ToolResult.FromText(result)));
        }

        return registry;
    }

    private sealed class StubDelegationHandler(string result) : IAgentDelegationHandler
    {
        public Task<ToolResult> DelegateAsync(
            AgentDelegationRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ToolResult.FromText(result));
        }
    }

    private static IReadOnlyList<DenyRule> ParseDenyRules(EvaluationScenario scenario)
    {
        return scenario.DenyRules.Select(rule =>
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern))
                throw new InvalidOperationException($"Scenario '{scenario.Id}' contains an empty deny rule.");
            if (!Enum.TryParse<DenyMatchMode>(rule.Mode, ignoreCase: true, out var mode))
                throw new InvalidOperationException($"Scenario '{scenario.Id}' contains unknown deny rule mode '{rule.Mode}'.");
            return new DenyRule(rule.Pattern, mode);
        }).ToArray();
    }

    private static List<string> VerifyRestoredArtifacts(
        EvaluationScenario scenario,
        AgentToolEndEvent[] toolEnds,
        IMessageStorage? storage,
        string sessionId)
    {
        var failures = new List<string>();
        var assertion = scenario.Assertions.RestoredArtifact;
        if (assertion == null)
            return failures;

        if (storage == null)
        {
            failures.Add("Restored artifact assertions require 'useMessageStorage: true'.");
            return failures;
        }

        var records = storage.GetMessagesAsync(sessionId).GetAwaiter().GetResult();
        var restored = records.Where(record => record.ToolResultState?.Artifact != null).ToArray();
        if (restored.Length == 0)
        {
            failures.Add("Expected the restored session to contain at least one artifact reference.");
            return failures;
        }

        foreach (var record in restored)
        {
            var artifact = record.ToolResultState!.Artifact!;
            var eventArtifact = toolEnds.FirstOrDefault(toolEnd => toolEnd.Artifact?.Id == artifact.Id)?.Artifact;
            if (eventArtifact == null)
                failures.Add($"Restored artifact '{artifact.Id}' has no matching AgentToolEndEvent artifact reference.");
            else if (!string.Equals(eventArtifact.Path, artifact.Path, StringComparison.Ordinal))
                failures.Add(
                    $"Restored artifact path '{artifact.Path}' differs from the event path '{eventArtifact.Path}'.");

            if (!File.Exists(artifact.Path))
            {
                failures.Add($"Restored artifact file '{artifact.Path}' does not exist.");
                continue;
            }

            var content = File.ReadAllText(artifact.Path);
            foreach (var expected in assertion.Contains.Where(expected => !content.Contains(expected, StringComparison.Ordinal)))
                failures.Add($"Restored artifact '{artifact.Id}' does not contain '{expected}'.");
            foreach (var forbidden in assertion.NotContains.Where(forbidden => content.Contains(forbidden, StringComparison.Ordinal)))
                failures.Add($"Restored artifact '{artifact.Id}' leaked forbidden text '{forbidden}'.");
        }

        return failures;
    }

    private static EvaluationWorkspace PrepareWorkspace(EvaluationScenario scenario)
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "insighta-agent-evals-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        if (string.IsNullOrWhiteSpace(scenario.Fixture))
        {
            return new EvaluationWorkspace(
                rootDirectory,
                FixtureDirectory: null,
                ArtifactDirectory: Path.Combine(rootDirectory, "artifacts"),
                StorageDirectory: Path.Combine(rootDirectory, "storage"));
        }

        var fixtureSource = Path.Combine(AppContext.BaseDirectory, "Fixtures", scenario.Fixture);
        if (!Directory.Exists(fixtureSource))
            throw new InvalidOperationException($"Fixture '{scenario.Fixture}' was not found at '{fixtureSource}'.");

        var fixtureTarget = Path.Combine(rootDirectory, "fixture");
        Directory.CreateDirectory(fixtureTarget);
        CopyDirectory(fixtureSource, fixtureTarget);
        return new EvaluationWorkspace(
            rootDirectory,
            fixtureTarget,
            ArtifactDirectory: Path.Combine(rootDirectory, "artifacts"),
            StorageDirectory: Path.Combine(rootDirectory, "storage"));
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    private static IReadOnlyList<RecordedLlmStep> ExpandFixturePlaceholders(
        IReadOnlyList<RecordedLlmStep> steps,
        string? fixtureDirectory)
    {
        if (fixtureDirectory == null)
            return steps;

        return steps.Select(step => new RecordedLlmStep
        {
            Text = step.Text,
            ToolCalls = step.ToolCalls.Select(toolCall => new RecordedToolCall
            {
                Id = toolCall.Id,
                Name = toolCall.Name,
                Arguments = toolCall.Arguments.ToDictionary(
                    pair => pair.Key,
                    pair => ExpandFixturePlaceholder(pair.Value, fixtureDirectory))
            }).ToList()
        }).ToList();
    }

    private static object? ExpandFixturePlaceholder(object? value, string fixtureDirectory)
    {
        return value switch
        {
            string text => text.Replace("${fixture}", fixtureDirectory, StringComparison.Ordinal),
            JsonElement { ValueKind: JsonValueKind.String } json =>
                json.GetString()?.Replace("${fixture}", fixtureDirectory, StringComparison.Ordinal),
            _ => value
        };
    }

    private static List<string> Verify(
        EvaluationScenario scenario,
        AgentTurnEndEvent? completed,
        IReadOnlyList<AgentToolStartEvent> toolStarts,
        IReadOnlyList<AgentToolEndEvent> toolEnds,
        IReadOnlyList<AgentEvent> events,
        string? fixtureDirectory,
        IMessageStorage? storage,
        AgentContextCompactedEvent[] compactedEvents)
    {
        var failures = new List<string>();
        if (completed == null)
        {
            failures.Add("Agent did not emit a turn-end event.");
            return failures;
        }

        if (!string.Equals(completed.Result.Status.ToString(), scenario.Assertions.Status, StringComparison.OrdinalIgnoreCase))
            failures.Add($"Expected status '{scenario.Assertions.Status}', got '{completed.Result.Status}'.");

        var actualTools = toolStarts.Select(x => x.ToolName).ToArray();
        if (!actualTools.SequenceEqual(scenario.Assertions.ToolCalls, StringComparer.Ordinal))
            failures.Add($"Expected tool calls [{string.Join(", ", scenario.Assertions.ToolCalls)}], got [{string.Join(", ", actualTools)}].");

        var actualToolErrors = toolEnds.Where(x => x.IsError).Select(x => x.ToolName).ToArray();
        if (!actualToolErrors.SequenceEqual(scenario.Assertions.ToolErrors, StringComparer.Ordinal))
        {
            failures.Add($"Expected tool errors [{string.Join(", ", scenario.Assertions.ToolErrors)}], got [{string.Join(", ", actualToolErrors)}].");
        }
        foreach (var failedTool in toolEnds.Where(x => x.IsError && !scenario.Assertions.ToolErrors.Contains(x.ToolName, StringComparer.Ordinal)))
            failures.Add($"Tool '{failedTool.ToolName}' failed: {failedTool.ResultPreview ?? "<no preview>"}");

        foreach (var toolName in scenario.Assertions.ArtifactTools)
        {
            if (!toolEnds.Any(toolEnd =>
                    string.Equals(toolEnd.ToolName, toolName, StringComparison.Ordinal) && toolEnd.Artifact != null))
            {
                failures.Add($"Expected tool '{toolName}' to expose a persisted result artifact.");
            }
        }

        if (scenario.Assertions.FinalText is { } expectedText)
        {
            var actualText = completed.Result.Message?.Content.OfType<TextBlock>().FirstOrDefault()?.Text;
            if (!string.Equals(expectedText, actualText, StringComparison.Ordinal))
                failures.Add($"Expected final text '{expectedText}', got '{actualText ?? "<none>"}'.");
        }

        foreach (var fileAssertion in scenario.Assertions.Files)
        {
            if (fixtureDirectory == null)
            {
                failures.Add($"File assertion '{fileAssertion.Path}' requires a fixture.");
                continue;
            }

            var path = Path.Combine(fixtureDirectory, fileAssertion.Path);
            if (!File.Exists(path))
            {
                failures.Add($"Expected fixture file '{fileAssertion.Path}' was not found.");
                continue;
            }

            var content = File.ReadAllText(path);
            foreach (var expected in fileAssertion.Contains.Where(expected => !content.Contains(expected, StringComparison.Ordinal)))
                failures.Add($"Expected fixture file '{fileAssertion.Path}' to contain '{expected}'.");
            foreach (var forbidden in fileAssertion.NotContains.Where(forbidden => content.Contains(forbidden, StringComparison.Ordinal)))
                failures.Add($"Expected fixture file '{fileAssertion.Path}' not to contain '{forbidden}'.");
        }

        var eventText = GetPublicEventText(events);
        foreach (var expected in scenario.Assertions.RequiredEventText.Where(expected => !eventText.Contains(expected, StringComparison.Ordinal)))
            failures.Add($"Expected public Agent events to contain '{expected}'.");
        foreach (var forbidden in scenario.Assertions.ForbiddenEventText.Where(forbidden => eventText.Contains(forbidden, StringComparison.Ordinal)))
            failures.Add($"Public Agent events leaked forbidden text '{forbidden}'.");

        var artifactText = string.Join('\n', toolEnds
            .Where(toolEnd => toolEnd.Artifact != null && File.Exists(toolEnd.Artifact.Path))
            .Select(toolEnd => File.ReadAllText(toolEnd.Artifact!.Path)));
        foreach (var expected in scenario.Assertions.RequiredArtifactText.Where(expected => !artifactText.Contains(expected, StringComparison.Ordinal)))
            failures.Add($"Expected persisted tool results to contain '{expected}'.");
        foreach (var forbidden in scenario.Assertions.ForbiddenArtifactText.Where(forbidden => artifactText.Contains(forbidden, StringComparison.Ordinal)))
            failures.Add($"Persisted tool results leaked forbidden text '{forbidden}'.");

        failures.AddRange(VerifyRestoredArtifacts(scenario, toolEnds.ToArray(), storage, $"eval-{scenario.Id}"));

        if (scenario.Assertions.CompactStrategy is { } expectedStrategy)
        {
            if (compactedEvents.Length == 0)
                failures.Add($"Expected a context compaction event with strategy '{expectedStrategy}', but none fired.");
            else if (!compactedEvents.Any(e => e.Strategy.Contains(expectedStrategy, StringComparison.Ordinal)))
                failures.Add(
                    $"Expected compaction strategy to contain '{expectedStrategy}', got [{string.Join(", ", compactedEvents.Select(e => e.Strategy))}].");

            if (scenario.Assertions.MaxPostCompactTokens is { } maxPost)
                foreach (var e in compactedEvents.Where(e => e.PostCompactTokens > maxPost))
                    failures.Add(
                        $"Compacted event '{e.Strategy}' post-compact tokens {e.PostCompactTokens} exceed the diagnostic budget {maxPost}.");
        }

        return failures;
    }

    private static string GetPublicEventText(IEnumerable<AgentEvent> events)
    {
        return string.Join('\n', events.SelectMany(agentEvent => agentEvent switch
        {
            AgentToolStartEvent toolStart => [toolStart.Arguments],
            AgentToolEndEvent toolEnd => [toolEnd.ResultPreview ?? string.Empty],
            AgentLlmStreamEvent { StreamEvent: TextDeltaEvent text } => [text.Delta],
            AgentLlmStreamEvent { StreamEvent: ThinkingDeltaEvent thinking } => [thinking.Delta],
            AgentErrorEvent error => [error.ErrorMessage],
            _ => Array.Empty<string>()
        }));
    }

    private sealed record EvaluationWorkspace(
        string RootDirectory,
        string? FixtureDirectory,
        string ArtifactDirectory,
        string StorageDirectory);

    /// <summary>Simulates a user approving a tool for the rest of the session.</summary>
    private sealed class AlwaysAllowHook(string toolName) : IToolHook
    {
        public IReadOnlyList<string> TargetTools { get; } = [toolName];

        public Task<ToolHookResult> OnBeforeExecutionAsync(
            string toolName,
            string arguments,
            ToolExecutionContext context)
        {
            return Task.FromResult(ToolHookResult.AllowAlways);
        }
    }
}
