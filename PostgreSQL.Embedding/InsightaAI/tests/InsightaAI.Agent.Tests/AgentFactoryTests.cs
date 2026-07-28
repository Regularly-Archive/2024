using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Cli.Services;
using InsightaAI.Agent.Context.Summary;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Skills;
using InsightaAI.Agent.Storage;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace InsightaAI.Agent.Tests;

public sealed class AgentFactoryTests
{
    [Fact]
    public async Task CreateAsync_Should_Expose_AgentServices_To_Tools()
    {
        var storage = new JsonlMessageStorage(Path.Combine(Path.GetTempPath(), "insighta-agent-factory-tests", Guid.NewGuid().ToString("N")));
        var servicesResolved = false;
        var toolRegistry = new ToolRegistry();
        toolRegistry.RegisterFunction(
            "inspect_agent_services",
            "Inspect services available to the current agent.",
            JsonDocument.Parse("{}").RootElement.Clone(),
            (_, context) =>
            {
                servicesResolved =
                    context.Services?.GetService<AgentConfig>() is not null &&
                    ReferenceEquals(context.Services.GetService<IMessageStorage>(), storage);
                return Task.FromResult(ToolResult.FromText("inspected"));
            });

        var toolCall = new ToolCallBlock
        {
            Id = "call-inspect",
            Name = "inspect_agent_services",
            Arguments = JsonDocument.Parse("{}").RootElement.Clone()
        };
        using var llmClient = new MockLlmClient(
            firstResponseToolCalls: [toolCall],
            secondResponse: "done");

        var config = new CliConfig
        {
            PrimaryModel = "test/model",
            MaxToolRounds = 2
        };
        var summaryService = new SummaryService(new SummaryOptions
        {
            Model = "test/model",
            ClientFactory = _ => new MockLlmClient()
        });
        var factory = new AgentFactory(storage);

        using var agent = await factory.CreateAsync(new AgentCreationOptions
        {
            Config = config,
            Auth = new AuthConfig(),
            LlmClient = llmClient,
            Model = new ModelEntry
            {
                ModelId = "test-model",
                MaxTokens = 128,
                ContextWindow = 4096
            },
            ToolRegistry = toolRegistry,
            SkillRegistry = new SkillRegistry(),
            SummaryService = summaryService,
            SessionId = "factory-test-session"
        });

        var result = await agent.RunAsync("Inspect the agent services.");

        Assert.Equal(AgentStatus.Completed, result.Status);
        Assert.True(servicesResolved);
    }
}
