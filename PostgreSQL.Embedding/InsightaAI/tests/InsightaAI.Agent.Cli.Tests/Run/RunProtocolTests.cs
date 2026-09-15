using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Cli.Run;
using InsightaAI.Agent.Cli.Services;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Storage;
using InsightaAI.Agents.Subagents.Definitions;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace InsightaAI.Agent.Cli.Tests;

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

    [Fact]
    public async Task RunApplication_Should_Run_One_Turn_And_Emit_Existing_AgentEvents()
    {
        var root = Path.Combine(Path.GetTempPath(), "insighta-run-protocol-tests", Guid.NewGuid().ToString("N"));
        var storage = new JsonlMessageStorage(root);
        using var loggerFactory = LoggerFactory.Create(_ => { });
        using var llm = new MockLlmClient(response: "run completed");
        var agentFactory = new AgentFactory(storage, loggerFactory);
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
            agentFactory,
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
        var agentFactory = new AgentFactory(storage, loggerFactory);
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
            config,
            auth,
            (_, _) => llm,
            (profileId, _) => ValueTask.FromResult<SubagentDefinition?>(
                string.Equals(profileId, "runner") ? profile : null));
        using var output = new StringWriter();

        var result = await application.ExecuteAsync(new RunRequest { Input = "run profile" }, output);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        var agentRequest = Assert.Single(llm.StreamingRequests);
        var toolNames = agentRequest.Tools.Select(tool => tool.Name).ToArray();
        Assert.Single(toolNames, "read_file");
        Assert.DoesNotContain("delegate", toolNames);
        Assert.DoesNotContain("bash", toolNames);
        await storage.DeleteSessionAsync(result.SessionId!);
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
