using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Cli.Hooks;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Cli.Services;
using InsightaAI.Agent.Context.Summary;
using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Memory;
using InsightaAI.Agent.Mcp;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Security;
using InsightaAI.Agent.Skills;
using InsightaAI.Agent.Storage;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InsightaAI.Agent.Tests;

public sealed class AgentFactoryTests
{
    [Fact]
    public void CliEnvironment_Should_Prefer_Process_Values_Over_Configured_Values()
    {
        const string variableName = "INSIGHTA_TEST_ENVIRONMENT_READER";
        var originalValue = Environment.GetEnvironmentVariable(variableName);

        try
        {
            Environment.SetEnvironmentVariable(variableName, "process");
            var environment = new CliEnvironment(new Dictionary<string, string>
            {
                [variableName] = "configured"
            });

            Assert.Equal("process", environment.Get(variableName));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, originalValue);
        }
    }

    [Fact]
    public async Task CreateAsync_Should_Expose_AgentServices_To_Tools()
    {
        // 进程环境可能启用 INSIGHTA_TELEMETRY（AgentFactory 据此装配 telemetry），
        // 而测试进程未初始化 TracerProvider，导致 ActivitySource.StartActivity 返回 null。
        // 注册全局 ActivityListener 提供最小可用的 telemetry 环境，让装配的组件正常工作。
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(activityListener);

        var storage = new JsonlMessageStorage(Path.Combine(Path.GetTempPath(), "insighta-agent-factory-tests", Guid.NewGuid().ToString("N")));
        // 模拟真实 CLI 流程：先创建会话（生成会话目录），再创建 Agent，确保 Agent 写消息时目录已存在
        var session = await storage.CreateSessionAsync("test-model", "test-provider");
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
                    ReferenceEquals(context.Services.GetService<IMessageStorage>(), storage) &&
                    context.Services.GetService<IMemoryManager>() is not null &&
                    context.Services.GetService<IEnvironmentVariableReader>()?.Get("TEST_AGENT_ENV") == "configured";
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
            MaxToolRounds = 2,
            Envs = new Dictionary<string, string>
            {
                ["TEST_AGENT_ENV"] = "configured"
            }
        };
        var summaryService = new SummaryService(new SummaryOptions
        {
            Model = "test/model",
            ClientFactory = _ => new MockLlmClient()
        });
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var factory = new AgentFactory(storage, loggerFactory);

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
            SessionId = session.Id
        });

        // 与真实 CLI 流程一致：context 携带 SessionId，Agent 写消息时使用该目录
        var result = await agent.RunAsync("Inspect the agent services.", new InsightaAI.Agent.Models.AgentContext { SessionId = session.Id });

        Assert.Equal(AgentStatus.Completed, result.Status);
        Assert.True(servicesResolved);
        Assert.NotNull(toolRegistry.GetExecutor("search_memory"));
        Assert.NotNull(toolRegistry.GetExecutor("save_memory"));
    }

    [Fact]
    public async Task CreateAsync_ShouldUseValidatedProfileIdentityButKeepCliSecurity()
    {
        var storage = new JsonlMessageStorage(Path.Combine(Path.GetTempPath(), "insighta-agent-profile-tests", Guid.NewGuid().ToString("N")));
        using var loggerFactory = LoggerFactory.Create(_ => { });
        using var llmClient = new MockLlmClient();
        var factory = new AgentFactory(storage, loggerFactory);
        var profile = new AgentConfig
        {
            Id = "explorer",
            Name = "Explorer",
            Model = "test-model",
            CustomInstructions = "Read-only exploration.",
            MaxToolRounds = 1,
            DenyRules = [new DenyRule("ignored", DenyMatchMode.Exact)]
        };

        using var agent = await factory.CreateAsync(new AgentCreationOptions
        {
            Config = new CliConfig { PrimaryModel = "test/model", MaxToolRounds = 4 },
            Auth = new AuthConfig(),
            LlmClient = llmClient,
            Model = new ModelEntry { ModelId = "test-model", MaxTokens = 128, ContextWindow = 4096 },
            ToolRegistry = new ToolRegistry(),
            SkillRegistry = new SkillRegistry(),
            SummaryService = new SummaryService(new SummaryOptions { Model = "test/model", ClientFactory = _ => new MockLlmClient() }),
            AgentConfigOverride = profile
        });

        Assert.Equal("explorer", agent.Config.Id);
        Assert.Equal("Explorer", agent.Config.Name);
        Assert.Equal("Read-only exploration.", agent.Config.CustomInstructions);
        Assert.Equal(1, agent.Config.MaxToolRounds);
        Assert.Empty(agent.Config.DenyRules);
        Assert.False(string.IsNullOrWhiteSpace(agent.Config.UserId));
    }

    [Fact]
    public async Task CreateAsync_PreAuthorizedAgent_ShouldSkipInteractivePermissionButKeepSecurityHook()
    {
        var storage = new JsonlMessageStorage(Path.Combine(Path.GetTempPath(), "insighta-agent-permission-tests", Guid.NewGuid().ToString("N")));
        using var loggerFactory = LoggerFactory.Create(_ => { });
        using var llmClient = new MockLlmClient();
        var factory = new AgentFactory(storage, loggerFactory);

        using var agent = await factory.CreateAsync(new AgentCreationOptions
        {
            Config = new CliConfig { PrimaryModel = "test/model" },
            Auth = new AuthConfig(),
            LlmClient = llmClient,
            Model = new ModelEntry { ModelId = "test-model", MaxTokens = 128, ContextWindow = 4096 },
            ToolRegistry = new ToolRegistry(),
            SkillRegistry = new SkillRegistry(),
            SummaryService = new SummaryService(new SummaryOptions { Model = "test/model", ClientFactory = _ => new MockLlmClient() }),
            EnableInteractiveToolPermission = false
        });

        var hooks = GetToolHooks(agent);

        Assert.DoesNotContain(hooks, hook => hook is ToolPermissionHook);
        Assert.Contains(hooks, hook => hook is SecurityPolicyHook);
    }

    [Fact]
    public async Task CreateAsync_Subagent_ShouldForceParallelToolExecution()
    {
        var storage = new JsonlMessageStorage(Path.Combine(Path.GetTempPath(), "insighta-agent-parallel-tests", Guid.NewGuid().ToString("N")));
        using var loggerFactory = LoggerFactory.Create(_ => { });
        using var llmClient = new MockLlmClient();
        var factory = new AgentFactory(storage, loggerFactory);

        using var agent = await factory.CreateAsync(new AgentCreationOptions
        {
            Config = new CliConfig { PrimaryModel = "test/model", ParallelToolExecution = false },
            Auth = new AuthConfig(),
            LlmClient = llmClient,
            Model = new ModelEntry { ModelId = "test-model", MaxTokens = 128, ContextWindow = 4096 },
            ToolRegistry = new ToolRegistry(),
            SkillRegistry = new SkillRegistry(),
            SummaryService = new SummaryService(new SummaryOptions { Model = "test/model", ClientFactory = _ => new MockLlmClient() }),
            EnableInteractiveToolPermission = false
        });

        Assert.True(agent.Config.ParallelToolExecution);
    }

    [Fact]
    public async Task CreateAsync_SubagentWithProfile_ShouldForceParallelToolExecution()
    {
        var storage = new JsonlMessageStorage(Path.Combine(Path.GetTempPath(), "insighta-agent-parallel-profile-tests", Guid.NewGuid().ToString("N")));
        using var loggerFactory = LoggerFactory.Create(_ => { });
        using var llmClient = new MockLlmClient();
        var factory = new AgentFactory(storage, loggerFactory);

        using var agent = await factory.CreateAsync(new AgentCreationOptions
        {
            Config = new CliConfig { PrimaryModel = "test/model", ParallelToolExecution = false },
            Auth = new AuthConfig(),
            LlmClient = llmClient,
            Model = new ModelEntry { ModelId = "test-model", MaxTokens = 128, ContextWindow = 4096 },
            ToolRegistry = new ToolRegistry(),
            SkillRegistry = new SkillRegistry(),
            SummaryService = new SummaryService(new SummaryOptions { Model = "test/model", ClientFactory = _ => new MockLlmClient() }),
            EnableInteractiveToolPermission = false,
            AgentConfigOverride = new AgentConfig
            {
                Id = "subagent",
                Name = "Subagent",
                Model = "test-model",
                ParallelToolExecution = false
            }
        });

        Assert.True(agent.Config.ParallelToolExecution);
    }

    [Fact]
    public async Task CreateAsync_ParentAgent_ShouldRespectConfigParallelToolExecution()
    {
        var storage = new JsonlMessageStorage(Path.Combine(Path.GetTempPath(), "insighta-agent-parent-parallel-tests", Guid.NewGuid().ToString("N")));
        using var loggerFactory = LoggerFactory.Create(_ => { });
        using var llmClient = new MockLlmClient();
        var factory = new AgentFactory(storage, loggerFactory);

        using var agent = await factory.CreateAsync(new AgentCreationOptions
        {
            Config = new CliConfig { PrimaryModel = "test/model", ParallelToolExecution = false },
            Auth = new AuthConfig(),
            LlmClient = llmClient,
            Model = new ModelEntry { ModelId = "test-model", MaxTokens = 128, ContextWindow = 4096 },
            ToolRegistry = new ToolRegistry(),
            SkillRegistry = new SkillRegistry(),
            SummaryService = new SummaryService(new SummaryOptions { Model = "test/model", ClientFactory = _ => new MockLlmClient() })
        });

        Assert.False(agent.Config.ParallelToolExecution);
    }

    [Fact]
    public async Task CreateAsync_ExcludedToolGroups_ShouldKeepServicesButHideTools()
    {
        var storage = new JsonlMessageStorage(Path.Combine(Path.GetTempPath(), "insighta-agent-capability-tests", Guid.NewGuid().ToString("N")));
        using var loggerFactory = LoggerFactory.Create(_ => { });
        using var llmClient = new MockLlmClient();
        var factory = new AgentFactory(storage, loggerFactory);
        var tools = new ToolRegistry();

        using var agent = await factory.CreateAsync(new AgentCreationOptions
        {
            Config = new CliConfig { PrimaryModel = "test/model" },
            Auth = new AuthConfig(),
            LlmClient = llmClient,
            Model = new ModelEntry { ModelId = "test-model", MaxTokens = 128, ContextWindow = 4096 },
            ToolRegistry = tools,
            SkillRegistry = new SkillRegistry(),
            SummaryService = new SummaryService(new SummaryOptions { Model = "test/model", ClientFactory = _ => new MockLlmClient() }),
            AgentConfigOverride = new AgentConfig
            {
                Id = "restricted",
                Name = "Restricted",
                Model = "test-model",
                ExcludedToolNames =
                [
                    "activate_skill", "list_skills",
                    "list_mcp_tools", "activate_mcp_tool", "deactivate_mcp_tool",
                    "save_memory", "update_memory", "delete_memory", "search_memory", "get_user_profile"
                ]
            }
        });

        Assert.Null(tools.GetExecutor("activate_skill"));
        Assert.Null(tools.GetExecutor("list_skills"));
        Assert.Null(tools.GetExecutor("list_mcp_tools"));
        Assert.Null(tools.GetExecutor("activate_mcp_tool"));
        Assert.Null(tools.GetExecutor("deactivate_mcp_tool"));
        Assert.Null(tools.GetExecutor("search_memory"));
        Assert.Null(tools.GetExecutor("save_memory"));
        Assert.NotNull(GetAgentServiceProvider(agent).GetService<IMemoryManager>());
        Assert.NotNull(GetAgentServiceProvider(agent).GetService<ISkillRegistry>());
        Assert.NotNull(GetAgentServiceProvider(agent).GetService<McpRegistry>());
    }

    private static IReadOnlyList<IToolHook> GetToolHooks(Agent agent)
    {
        var field = typeof(Agent).GetField("_toolHooks", BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsAssignableFrom<IReadOnlyList<IToolHook>>(field?.GetValue(agent));
    }

    private static IServiceProvider GetAgentServiceProvider(Agent agent)
    {
        var field = typeof(Agent).GetField("_serviceProvider", BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsAssignableFrom<IServiceProvider>(field?.GetValue(agent));
    }
}
