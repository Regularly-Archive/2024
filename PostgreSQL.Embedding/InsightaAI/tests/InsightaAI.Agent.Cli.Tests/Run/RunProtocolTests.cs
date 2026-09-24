using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Cli.Run;
using InsightaAI.Agent.Cli.Services;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Storage;
using InsightaAI.Agents.Subagents.Definitions;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;
using InsightaAI.Agent.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace InsightaAI.Agent.Cli.Tests;

[Collection("Run telemetry")]
public sealed class RunProtocolTests
{
    [Fact]
    public async Task JsonlWriter_Should_Serialize_Existing_AgentEvent_Without_Remapping()
    {
        using var stringWriter = new StringWriter();
        var writer = new RunJsonlWriter(stringWriter);
        AgentEvent agentEvent = new AgentLlmStreamEvent
        {
            AgentId = "agent-1",
            StreamEvent = new TextDeltaEvent
            {
                Delta = "hello",
                ContentIndex = 0
            }
        };

        await writer.RunStartedAsync("session-1", "test-model", null);
        await writer.AgentEventAsync(agentEvent);
        await writer.RunCompletedAsync(
            "session-1",
            new AgentResult
            {
                Status = AgentStatus.Completed,
                Message = InsightaAI.LLM.Models.Message.FromAssistant("hello"),
                Usage = new TokenUsage { InputTokens = 1, OutputTokens = 2 },
                Rounds = 1,
                DurationMs = 12
            },
            ExitCodes.Success);

        var lines = stringWriter.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        var started = JsonDocument.Parse(lines[0]).RootElement;
        var eventEnvelope = JsonDocument.Parse(lines[1]).RootElement;
        var completed = JsonDocument.Parse(lines[2]).RootElement;

        Assert.Equal("run.started", started.GetProperty("type").GetString());
        Assert.Equal("agent.event", eventEnvelope.GetProperty("type").GetString());
        var payload = eventEnvelope.GetProperty("event");
        Assert.Equal("agentLlmStream", payload.GetProperty("$type").GetString());
        Assert.Equal("llmStream", payload.GetProperty("type").GetString());
        Assert.Equal("textDelta", payload.GetProperty("streamEvent").GetProperty("$type").GetString());
        Assert.Equal("hello", payload.GetProperty("streamEvent").GetProperty("delta").GetString());
        Assert.Equal("run.completed", completed.GetProperty("type").GetString());
        Assert.Equal("completed", completed.GetProperty("result").GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunApplication_Should_Run_One_Turn_And_Emit_Existing_AgentEvents(bool useToolAndTelemetry)
    {
        var root = Path.Combine(Path.GetTempPath(), "insighta-run-protocol-tests", Guid.NewGuid().ToString("N"));
        var storage = new JsonlMessageStorage(root);
        using var loggerFactory = LoggerFactory.Create(_ => { });
        using var llm = new MockLlmClient(response: "run completed",
            firstResponseToolCalls: useToolAndTelemetry
                ? [new ToolCallBlock { Id = "clock-call", Name = "whereami", Arguments = JsonSerializer.SerializeToElement(new { }) }]
                : null);
        var agentFactory = new AgentFactory(storage, loggerFactory, new InsightaAI.LLM.LlmClientFactory());
        var config = new CliConfig
        {
            PrimaryModel = "mock/test-model",
            ParallelToolExecution = false
        };
        config.Models["mock/test-model"] = new ModelEntry
        {
            ModelId = "test-model",
            MaxTokens = 128,
            ContextWindow = 4096
        };
        var auth = new AuthConfig();
        auth.Providers["mock"] = new ProviderEntry();

        var application = new RunApplication(
            storage,
            useToolAndTelemetry ? new TelemetryAgentFactory(agentFactory) : agentFactory,
            new InsightaAI.LLM.LlmClientFactory(),
            config,
            auth,
            (_, _) => llm);
        using var output = new StringWriter();

        var result = await application.ExecuteAsync(new RunRequest { Input = "say hello" }, output);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.NotNull(result.SessionId);
        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var types = lines.Select(line => JsonDocument.Parse(line).RootElement.GetProperty("type").GetString()).ToArray();

        Assert.Equal("run.started", types[0]);
        Assert.Equal("run.completed", types[^1]);
        Assert.Contains("agent.event", types);
        var agentPayloads = lines
            .Where(line => JsonDocument.Parse(line).RootElement.GetProperty("type").GetString() == "agent.event")
            .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("event").GetProperty("$type").GetString())
            .ToArray();
        Assert.Contains("agentTurnStart", agentPayloads);
        Assert.Contains("agentRoundStart", agentPayloads);
        Assert.Contains("agentLlmStream", agentPayloads);
        Assert.Contains("agentRoundEnd", agentPayloads);
        Assert.Contains("agentTurnEnd", agentPayloads);
        if (useToolAndTelemetry)
        {
            Assert.Contains("agentToolStart", agentPayloads);
            Assert.Contains("agentToolEnd", agentPayloads);
            Assert.Equal(2, agentPayloads.Count(type => type == "agentRoundStart"));
            Assert.Equal(2, JsonDocument.Parse(lines[^1]).RootElement
                .GetProperty("result").GetProperty("rounds").GetInt32());
        }
        else
            Assert.DoesNotContain("agentToolStart", agentPayloads);

        await storage.DeleteSessionAsync(result.SessionId!);
    }

    [Fact]
    public async Task RunApplication_Should_Use_Profile_Allowed_Tools()
    {
        var root = Path.Combine(Path.GetTempPath(), "insighta-run-profile-tests", Guid.NewGuid().ToString("N"));
        var storage = new JsonlMessageStorage(root);
        using var loggerFactory = LoggerFactory.Create(_ => { });
        using var llm = new RecordingLlmClient();
        var agentFactory = new AgentFactory(storage, loggerFactory, new InsightaAI.LLM.LlmClientFactory());
        var config = new CliConfig
        {
            PrimaryModel = "mock/test-model",
            ParallelToolExecution = false
        };
        config.Models["mock/test-model"] = new ModelEntry
        {
            ModelId = "test-model",
            MaxTokens = 128,
            ContextWindow = 4096
        };
        var auth = new AuthConfig();
        auth.Providers["mock"] = new ProviderEntry();
        var profile = new InsightaSubagentDefinition
        {
            Id = "runner",
            Name = "Runner",
            ToolNames = ["read_file"]
        };

        var application = new RunApplication(
            storage,
            agentFactory,
            new InsightaAI.LLM.LlmClientFactory(),
            config,
            auth,
            (_, _) => llm,
            (profileId, _) => ValueTask.FromResult<SubagentDefinition?>(
                string.Equals(profileId, "runner") ? profile : null));
        using var output = new StringWriter();

        var result = await application.ExecuteAsync(new RunRequest
        {
            Input = "run profile",
            ProfileId = "runner",
            AllowedToolNames = ["read_file"]
        }, output);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        var agentRequest = Assert.Single(llm.StreamingRequests);
        var toolNames = agentRequest.Tools.Select(tool => tool.Name).ToArray();
        Assert.Single(toolNames, "read_file");
        Assert.DoesNotContain("delegate", toolNames);
        Assert.DoesNotContain("bash", toolNames);
        await storage.DeleteSessionAsync(result.SessionId!);
    }

    [Fact]
    public async Task RunApplication_Should_Reject_AllowedTools_Without_Profile()
    {
        var storage = new JsonlMessageStorage(Path.Combine(Path.GetTempPath(), "insighta-run-tests", Guid.NewGuid().ToString("N")));
        using var loggerFactory = LoggerFactory.Create(_ => { });
        using var llm = new MockLlmClient();
        var application = new RunApplication(storage, new AgentFactory(storage, loggerFactory, new InsightaAI.LLM.LlmClientFactory()), new InsightaAI.LLM.LlmClientFactory(), CreateConfig(), CreateAuth(), (_, _) => llm);
        using var output = new StringWriter();

        var result = await application.ExecuteAsync(new RunRequest
        {
            Input = "run",
            AllowedToolNames = ["read_file"]
        }, output);

        Assert.Equal(ExitCodes.Failed, result.ExitCode);
        var failed = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.Equal("run.failed", failed.GetProperty("type").GetString());
        Assert.Equal("--allowed-tools requires --profile.", failed.GetProperty("error").GetString());
        Assert.Empty(await storage.GetSessionsAsync());
    }

    [Fact]
    public async Task RunApplication_Should_Reject_Tools_Not_Allowed_By_Profile()
    {
        var storage = new JsonlMessageStorage(Path.Combine(Path.GetTempPath(), "insighta-run-tests", Guid.NewGuid().ToString("N")));
        using var loggerFactory = LoggerFactory.Create(_ => { });
        using var llm = new MockLlmClient();
        var profile = new InsightaSubagentDefinition { Id = "runner", Name = "Runner", ToolNames = ["read_file"] };
        var application = new RunApplication(
            storage, new AgentFactory(storage, loggerFactory, new InsightaAI.LLM.LlmClientFactory()), new InsightaAI.LLM.LlmClientFactory(), CreateConfig(), CreateAuth(), (_, _) => llm,
            (profileId, _) => ValueTask.FromResult<SubagentDefinition?>(profileId == "runner" ? profile : null));
        using var output = new StringWriter();

        var result = await application.ExecuteAsync(new RunRequest
        {
            Input = "run",
            ProfileId = "runner",
            AllowedToolNames = ["grep"]
        }, output);

        Assert.Equal(ExitCodes.Failed, result.ExitCode);
        var failed = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.Equal("run.failed", failed.GetProperty("type").GetString());
        Assert.Contains("'grep'", failed.GetProperty("error").GetString());
        Assert.Empty(await storage.GetSessionsAsync());
    }

    private static CliConfig CreateConfig()
    {
        var config = new CliConfig { PrimaryModel = "mock/test-model", ParallelToolExecution = false };
        config.Models["mock/test-model"] = new ModelEntry { ModelId = "test-model", MaxTokens = 128, ContextWindow = 4096 };
        return config;
    }

    private static AuthConfig CreateAuth()
    {
        var auth = new AuthConfig();
        auth.Providers["mock"] = new ProviderEntry();
        return auth;
    }

    private sealed class TelemetryAgentFactory(IAgentFactory inner) : IAgentFactory
    {
        public async Task<Agent> CreateAsync(AgentCreationOptions options, CancellationToken cancellationToken = default)
        {
            var agent = await inner.CreateAsync(options, cancellationToken);
            var hook = new AgentEventTelemetryHook();
            hook.SetSessionContext("cli-agent", "test", options.Model.ModelId, options.SessionId!);
            agent.AddAgentHook(hook);
            return agent;
        }
    }

    private sealed class RecordingLlmClient : InsightaAI.LLM.Abstractions.ILlmClient
    {
        public List<LlmRequest> StreamingRequests { get; } = [];
        public string AdapterName => "recording";
        public bool SupportsReasoning => false;

        public void Dispose()
        {
        }

        public LLM.Abstractions.LlmStream Streaming(LlmRequest request)
        {
            StreamingRequests.Add(request);
            return new MockLlmStream("profile completed");
        }

        public Task<LlmResponse> CompleteAsync(
            LlmRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test expects the Agent to stream.");
        }
    }
}
