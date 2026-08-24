using System.Text.Json;
using InsightaAI.Agent;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Cli.Services;
using InsightaAI.Agent.Context.Summary;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Skills;
using InsightaAI.Agent.Storage;
using InsightaAI.Agents.Subagents.Definitions;
using InsightaAI.Agents.Subagents.Invocation;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;

namespace InsightaAI.Agents.Subagents.Tests.Invocation;

public sealed class CliInsightaSubagentAdapterTests : IDisposable
{
    private readonly string _storagePath = Path.Combine(Path.GetTempPath(), "insighta-cli-subagent-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InvokeAsync_NewChildSession_PersistsInvocationAndExcludesDelegate()
    {
        var parentTools = new ToolRegistry().Register(new NamedTool("delegate"));
        var storage = new JsonlMessageStorage(_storagePath);
        var factory = new RecordingAgentFactory();
        var adapter = new CliInsightaSubagentAdapter(factory, storage, CreateTemplate(parentTools));

        var result = await adapter.InvokeAsync(CreateRequest(
            new InsightaSubagentDefinition { Id = "reviewer", Name = "Reviewer", ToolNames = ["delegate"] }));

        Assert.Equal("child-invocation", result.InvocationId);
        Assert.NotNull(result.SessionId);
        var session = await storage.GetSessionAsync(result.SessionId!);
        Assert.NotNull(session);
        Assert.Equal("parent-session", session!.ParentSessionId);
        Assert.Equal("parent-call", session.ParentInvocationId);
        Assert.Equal("child-invocation", session.InvocationId);
        Assert.NotNull(factory.Options);
        Assert.Null(factory.Options!.ToolRegistry.GetExecutor("delegate"));
    }

    [Fact]
    public async Task InvokeAsync_ParentSessionCannotBeReusedAsChildSession()
    {
        var storage = new JsonlMessageStorage(_storagePath);
        var parent = await storage.CreateSessionAsync("test-model", "test-provider", userId: "host-user");
        var adapter = new CliInsightaSubagentAdapter(new RecordingAgentFactory(), storage, CreateTemplate(new ToolRegistry()));
        var request = CreateRequest(new InsightaSubagentDefinition { Id = "reviewer", Name = "Reviewer" }) with
        {
            Context = new SubagentInvocationContext
            {
                InvocationId = "child-invocation",
                UserId = "host-user",
                SessionId = parent.Id,
                ParentSessionId = parent.Id,
                ParentInvocationId = "parent-call"
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.InvokeAsync(request));

        Assert.Contains("parent session", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatSessionLoadAsync_HidesChildSession()
    {
        var storage = new JsonlMessageStorage(_storagePath);
        var parent = await storage.CreateSessionAsync("test-model", "test-provider");
        var child = await storage.CreateSessionAsync(
            "test-model", "test-provider", parentSessionId: parent.Id);

        var loadedParent = await ChatSession.LoadAsync(storage, parent.Id);
        var loadedChild = await ChatSession.LoadAsync(storage, child.Id);

        Assert.NotNull(loadedParent);
        Assert.Null(loadedChild);
    }

    [Fact]
    public async Task InvokeAsync_IntersectsDefinitionAndRequestToolWhitelists()
    {
        var parentTools = new ToolRegistry()
            .Register(new NamedTool("read_file"))
            .Register(new NamedTool("grep"));
        var factory = new RecordingAgentFactory();
        var adapter = new CliInsightaSubagentAdapter(factory, new JsonlMessageStorage(_storagePath), CreateTemplate(parentTools));
        var request = CreateRequest(new InsightaSubagentDefinition
        {
            Id = "explorer",
            Name = "Explorer",
            ToolNames = ["read_file", "grep"]
        }) with { AllowedToolNames = ["grep"] };

        await adapter.InvokeAsync(request);

        Assert.NotNull(factory.Options);
        Assert.Null(factory.Options!.ToolRegistry.GetExecutor("read_file"));
        Assert.NotNull(factory.Options.ToolRegistry.GetExecutor("grep"));
    }

    [Fact]
    public async Task InvokeAsync_EnabledSkillCapability_ExposesSkillTools()
    {
        var hostTools = new ToolRegistry()
            .Register(new NamedTool("activate_skill"))
            .Register(new NamedTool("list_skills"));
        var factory = new RecordingAgentFactory();
        var adapter = new CliInsightaSubagentAdapter(factory, new JsonlMessageStorage(_storagePath), CreateTemplate(hostTools));
        var definition = new InsightaSubagentDefinition
        {
            Id = "reviewer",
            Name = "Reviewer",
            Capabilities = new InsightaSubagentCapabilities { EnableSkills = true }
        };

        await adapter.InvokeAsync(CreateRequest(definition));

        Assert.NotNull(factory.Options);
        Assert.DoesNotContain("activate_skill", factory.Options!.AgentConfigOverride!.ExcludedToolNames);
        Assert.DoesNotContain("list_skills", factory.Options.AgentConfigOverride.ExcludedToolNames);
        Assert.NotNull(factory.Options.ToolRegistry.GetExecutor("activate_skill"));
        Assert.NotNull(factory.Options.ToolRegistry.GetExecutor("list_skills"));
    }

    [Fact]
    public async Task InvokeAsync_MissingDescriptorTool_FailsBeforeCreatingChildSession()
    {
        var storage = new JsonlMessageStorage(_storagePath);
        var adapter = new CliInsightaSubagentAdapter(
            new RecordingAgentFactory(), storage, CreateTemplate(new ToolRegistry()));
        var definition = new InsightaSubagentDefinition
        {
            Id = "reviewer",
            Name = "Reviewer",
            ToolNames = ["write_file"]
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.InvokeAsync(CreateRequest(definition)));

        Assert.Contains("reviewer", exception.Message, StringComparison.Ordinal);
        Assert.Contains("write_file", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await storage.GetSessionsAsync());
    }

    [Fact]
    public async Task InvokeAsync_AlreadyCancelled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var adapter = new CliInsightaSubagentAdapter(
            new RecordingAgentFactory(), new JsonlMessageStorage(_storagePath), CreateTemplate(new ToolRegistry()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.InvokeAsync(
            CreateRequest(new InsightaSubagentDefinition { Id = "reviewer", Name = "Reviewer" }), cancellation.Token));
    }

    [Fact]
    public async Task InvokeAsync_ReportsChildProgressWithoutChangingTheFinalResult()
    {
        var progress = new RecordingProgressReporter();
        var adapter = new CliInsightaSubagentAdapter(
            new RecordingAgentFactory(), new JsonlMessageStorage(_storagePath), CreateTemplate(new ToolRegistry()));

        var result = await adapter.InvokeAsync(CreateRequest(
            new InsightaSubagentDefinition { Id = "reviewer", Name = "Reviewer" }) with
        {
            Progress = progress
        });

        Assert.Equal(SubagentInvocationStatus.Completed, result.Status);
        Assert.Equal("done", result.Output);
        Assert.Contains(progress.Updates, update => update.Kind == SubagentProgressKind.Started);
        Assert.Contains(progress.Updates, update =>
            update.Kind == SubagentProgressKind.Output && update.Text == "done");
    }

    [Fact]
    public async Task InvokeAsync_ForwardsChildToolOutputStream()
    {
        var toolCall = new ToolCallBlock
        {
            Id = "child-tool-call",
            Name = "report_progress",
            Arguments = JsonSerializer.SerializeToElement(new { })
        };
        var llmClient = new MockLlmClient(
            firstResponseToolCalls: [toolCall],
            secondResponse: "done");
        var factory = new RecordingAgentFactory(llmClient);
        var progress = new RecordingProgressReporter();
        var adapter = new CliInsightaSubagentAdapter(
            factory,
            new JsonlMessageStorage(_storagePath),
            CreateTemplate(new ToolRegistry().Register(new ProgressReportingTool())));

        await adapter.InvokeAsync(CreateRequest(new InsightaSubagentDefinition
        {
            Id = "reviewer",
            Name = "Reviewer",
            ToolNames = ["report_progress"]
        }) with { Progress = progress });

        Assert.Contains(progress.Updates, update =>
            update.Kind == SubagentProgressKind.Output &&
            update.Text == "child stderr" &&
            update.Stream == SubagentOutputStream.Stderr);
    }

    [Fact]
    public async Task InvokeAsync_NullToolNames_TreatsItAsAnEmptyWhitelist()
    {
        var factory = new RecordingAgentFactory();
        var adapter = new CliInsightaSubagentAdapter(
            factory, new JsonlMessageStorage(_storagePath), CreateTemplate(new ToolRegistry()));

        await adapter.InvokeAsync(CreateRequest(new InsightaSubagentDefinition
        {
            Id = "reviewer",
            Name = "Reviewer",
            ToolNames = null!
        }));

        Assert.NotNull(factory.Options);
        Assert.Empty(factory.Options!.ToolRegistry.GetDefinitions());
    }

    [Fact]
    public async Task InvokeAsync_WithoutModel_UsesConfiguredPrimaryModelRatherThanParentSwitchedModel()
    {
        var config = CreateConfig();
        config.Models["test/reviewer"] = new ModelEntry { ModelId = "reviewer-model", MaxTokens = 64, ContextWindow = 2048 };
        var template = CreateTemplate(new ToolRegistry(), config) with
        {
            Model = config.Models["test/reviewer"],
            LlmClient = new MockLlmClient(response: "parent switched model")
        };
        var factory = new RecordingAgentFactory();
        var adapter = new CliInsightaSubagentAdapter(factory, new JsonlMessageStorage(_storagePath), template);

        await adapter.InvokeAsync(CreateRequest(new InsightaSubagentDefinition { Id = "reviewer", Name = "Reviewer" }));

        Assert.NotNull(factory.Options);
        Assert.Equal("primary-model", factory.Options!.Model.ModelId);
        Assert.Equal("primary-model", factory.Options.AgentConfigOverride!.Model);
    }

    [Fact]
    public async Task InvokeAsync_WithModelReference_UsesThatConfiguredModel()
    {
        var config = CreateConfig();
        config.Models["test/reviewer"] = new ModelEntry { ModelId = "reviewer-model", MaxTokens = 64, ContextWindow = 2048 };
        var factory = new RecordingAgentFactory();
        var storage = new JsonlMessageStorage(_storagePath);
        var adapter = new CliInsightaSubagentAdapter(factory, storage, CreateTemplate(new ToolRegistry(), config));

        var result = await adapter.InvokeAsync(CreateRequest(new InsightaSubagentDefinition
        {
            Id = "reviewer",
            Name = "Reviewer",
            Model = "test/reviewer"
        }));

        Assert.NotNull(factory.Options);
        Assert.Equal("reviewer-model", factory.Options!.Model.ModelId);
        Assert.Equal("reviewer-model", factory.Options.AgentConfigOverride!.Model);
        var session = await storage.GetSessionAsync(result.SessionId!);
        Assert.Equal("reviewer-model", session!.Model);
        Assert.Equal("test", session.Provider);
    }

    public void Dispose()
    {
        if (Directory.Exists(_storagePath))
            Directory.Delete(_storagePath, recursive: true);
    }

    private static SubagentInvocationRequest CreateRequest(InsightaSubagentDefinition definition) => new()
    {
        Definition = definition,
        Input = "Review the current change.",
        Context = new SubagentInvocationContext
        {
            InvocationId = "child-invocation",
            UserId = "host-user",
            ParentSessionId = "parent-session",
            ParentInvocationId = "parent-call"
        }
    };

    private static AgentCreationOptions CreateTemplate(ToolRegistry toolRegistry, CliConfig? config = null) => new()
    {
        Config = config ?? CreateConfig(),
        Auth = CreateAuth(),
        LlmClient = new MockLlmClient(response: "done"),
        Model = new ModelEntry { ModelId = "test-model", MaxTokens = 128, ContextWindow = 4096 },
        ToolRegistry = toolRegistry,
        SkillRegistry = new SkillRegistry(),
        SummaryService = new SummaryService(new SummaryOptions { Model = "test/model", ClientFactory = _ => new MockLlmClient() })
    };

    private static CliConfig CreateConfig() => new()
    {
        PrimaryModel = "test/model",
        Models = new Dictionary<string, ModelEntry>
        {
            ["test/model"] = new() { ModelId = "primary-model", MaxTokens = 128, ContextWindow = 4096 }
        }
    };

    private static AuthConfig CreateAuth() => new()
    {
        Providers = new Dictionary<string, ProviderEntry>
        {
            ["test"] = new() { Adapter = "openai", ApiKey = "test-key" }
        }
    };

    private sealed class RecordingAgentFactory(ILlmClient? llmClient = null) : IAgentFactory
    {
        public AgentCreationOptions? Options { get; private set; }

        public Task<InsightaAI.Agent.Agent> CreateAsync(AgentCreationOptions options, CancellationToken cancellationToken = default)
        {
            Options = options;
            return Task.FromResult(new InsightaAI.Agent.Agent(
                options.AgentConfigOverride!,
                llmClient ?? new MockLlmClient(response: "done"),
                options.ToolRegistry,
                options.SkillRegistry));
        }
    }

    private sealed class NamedTool(string name) : ITool
    {
        public string Name { get; } = name;
        public ToolDefinition Definition { get; } = new()
        {
            Name = name,
            Description = name,
            Schema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { } })
        };

        public Task<ToolResult> ExecuteAsync(IDictionary<string, object> args, ToolExecutionContext context)
        {
            return Task.FromResult(ToolResult.FromText("done"));
        }
    }

    private sealed class ProgressReportingTool : ITool
    {
        public string Name => "report_progress";
        public ToolDefinition Definition { get; } = new()
        {
            Name = "report_progress",
            Description = "Reports stderr progress.",
            Schema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { } })
        };

        public async Task<ToolResult> ExecuteAsync(IDictionary<string, object> args, ToolExecutionContext context)
        {
            await context.Progress.ReportAsync(new ToolProgressUpdate
            {
                Kind = ToolProgressKind.Output,
                Text = "child stderr",
                Stream = ToolOutputStream.Stderr
            }, context.CancellationToken);
            return ToolResult.FromText("reported");
        }
    }

    private sealed class RecordingProgressReporter : ISubagentProgressReporter
    {
        public List<SubagentProgressUpdate> Updates { get; } = [];

        public ValueTask ReportAsync(SubagentProgressUpdate update, CancellationToken cancellationToken = default)
        {
            Updates.Add(update);
            return ValueTask.CompletedTask;
        }
    }
}
