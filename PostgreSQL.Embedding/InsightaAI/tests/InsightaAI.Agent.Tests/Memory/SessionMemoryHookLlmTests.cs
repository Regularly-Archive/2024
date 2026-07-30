using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Memory;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;

namespace InsightaAI.Agent.Tests.Memory;

/// <summary>
/// SessionMemoryHook LLM 摘要路径测试
/// </summary>
public class SessionMemoryHookLlmTests : IDisposable
{
    private readonly string _tempHome;
    private const string TestUserId = "test-user-llm";

    public SessionMemoryHookLlmTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), $"insightai_llm_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempHome);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempHome))
        {
            try { Directory.Delete(_tempHome, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public void Id_Should_ReturnSessionMemory()
    {
        var hook = new SessionMemoryHook("test-id", TestUserId,
            options: new SessionMemoryOptions { EnableLlmSummary = false });
        Assert.Equal("session-memory", hook.Id);
    }

    [Fact]
    public async Task LlmSummary_Should_TriggerAtCorrectRounds()
    {
        var sessionId = $"trigger-test-{Guid.NewGuid():N}";
        var llmClient = new MockLlmClient(response: "<summary>Test summary</summary>");
        var hook = new SessionMemoryHook(sessionId, TestUserId,
            options: new SessionMemoryOptions
            {
                EnableLlmSummary = true,
                MinRoundsBeforeLlm = 3,
                SummaryInterval = TimeSpan.Zero
            }, summaryService: CreateSummaryService(llmClient));

        var messages = new List<Message> { Message.FromUser("test message") };

        // Round 1, 2: 不应触发 LLM
        await hook.OnAgentRoundEndedAsync(CreateRoundEndContext(sessionId, 1), messages, null);
        await Task.Delay(300);
        await hook.OnAgentRoundEndedAsync(CreateRoundEndContext(sessionId, 2), messages, null);
        await Task.Delay(300);

        // Round 3: 应触发 LLM
        await hook.OnAgentRoundEndedAsync(CreateRoundEndContext(sessionId, 3), messages, null);
        await Task.Delay(500);
        var mem3 = await hook.GetSessionMemoryAsync();

        Assert.Contains("Test summary", mem3);
    }

    [Fact]
    public async Task LlmSummary_Should_ParseSummaryTags()
    {
        var sessionId = $"parse-test-{Guid.NewGuid():N}";

        var llmResponse = @"Some analysis text here...
<summary>
## Goal
- Implement session memory

## Progress
### Done
- [x] Created hook
</summary>";

        var llmClient = new MockLlmClient(response: llmResponse);
        var hook = new SessionMemoryHook(sessionId, TestUserId,
            options: new SessionMemoryOptions
            {
                EnableLlmSummary = true,
                MinRoundsBeforeLlm = 1,
                SummaryInterval = TimeSpan.Zero
            }, summaryService: CreateSummaryService(llmClient));

        var messages = new List<Message> { Message.FromUser("implement session memory") };

        await hook.OnAgentRoundEndedAsync(CreateRoundEndContext(sessionId, 1), messages, null);
        await Task.Delay(500);

        var memory = await hook.GetSessionMemoryAsync();

        Assert.Contains("## Goal", memory);
        Assert.Contains("Implement session memory", memory);
        Assert.DoesNotContain("Some analysis text here", memory);
    }

    [Fact]
    public async Task LlmSummary_Should_ReplaceFile_NotAppend()
    {
        var sessionId = $"replace-test-{Guid.NewGuid():N}";
        var llmClient = new MockLlmClient(
            response: "<summary>Round 1 summary</summary>",
            secondResponse: "<summary>Updated summary</summary>");
        var hook = new SessionMemoryHook(sessionId, TestUserId,
            options: new SessionMemoryOptions
            {
                EnableLlmSummary = true,
                MinRoundsBeforeLlm = 1,
                SummaryInterval = TimeSpan.Zero
            }, summaryService: CreateSummaryService(llmClient));

        var messages = new List<Message> { Message.FromUser("first round") };

        // Round 1
        await hook.OnAgentRoundEndedAsync(CreateRoundEndContext(sessionId, 1), messages, null);
        await Task.Delay(500);

        // Round 2: MockLlmClient 返回 secondResponse
        await hook.OnAgentRoundEndedAsync(CreateRoundEndContext(sessionId, 2), messages, null);
        await Task.Delay(500);
        var mem2 = await hook.GetSessionMemoryAsync();

        Assert.Contains("Updated summary", mem2);
        Assert.DoesNotContain("Round 1 summary", mem2);
    }

    [Fact]
    public async Task LlmSummary_Should_NotWriteMemory_WhenLlmReturnsEmpty()
    {
        var sessionId = $"empty-llm-{Guid.NewGuid():N}";
        var llmClient = new MockLlmClient(response: "");
        var hook = new SessionMemoryHook(sessionId, TestUserId,
            options: new SessionMemoryOptions
            {
                EnableLlmSummary = true,
                MinRoundsBeforeLlm = 1,
                SummaryInterval = TimeSpan.Zero
            }, summaryService: CreateSummaryService(llmClient));

        var messages = new List<Message> { Message.FromUser("test message") };

        await hook.OnAgentRoundEndedAsync(CreateRoundEndContext(sessionId, 1), messages, null);
        await Task.Delay(500);

        var memory = await hook.GetSessionMemoryAsync();

        // LLM 返回空内容时不写入文件，内存为空
        Assert.Equal("", memory);
    }

    [Fact]
    public async Task LlmSummary_Should_NotWriteMemory_WhenFactoryIsNull()
    {
        var sessionId = $"no-factory-{Guid.NewGuid():N}";
        var hook = new SessionMemoryHook(sessionId, TestUserId,
            options: new SessionMemoryOptions
            {
                EnableLlmSummary = true,
                MinRoundsBeforeLlm = 1,
                SummaryInterval = TimeSpan.Zero
            });

        var messages = new List<Message> { Message.FromUser("test message") };

        await hook.OnAgentRoundEndedAsync(CreateRoundEndContext(sessionId, 1), messages, null);
        await Task.Delay(500);

        var memory = await hook.GetSessionMemoryAsync();

        // 没有工厂 → LLM 分支不执行 → 无记忆写入
        Assert.Equal("", memory);
    }

    [Fact]
    public async Task LlmSummary_Should_PreserveAnchoredSummary_AcrossRounds()
    {
        var sessionId = $"anchored-{Guid.NewGuid():N}";
        var llmClient = new MockLlmClient(
            response: "<summary>## Goal\n- Build agent</summary>",
            secondResponse: "<summary>## Goal\n- Build agent\n\n## Progress\n### Done\n- [x] Started</summary>");
        var hook = new SessionMemoryHook(sessionId, TestUserId,
            options: new SessionMemoryOptions
            {
                EnableLlmSummary = true,
                MinRoundsBeforeLlm = 1,
                SummaryInterval = TimeSpan.Zero
            }, summaryService: CreateSummaryService(llmClient));

        var msgs1 = new List<Message> { Message.FromUser("build an agent") };
        await hook.OnAgentRoundEndedAsync(CreateRoundEndContext(sessionId, 1), msgs1, null);
        await Task.Delay(500);

        var msgs2 = new List<Message> { Message.FromUser("I started working on it") };
        await hook.OnAgentRoundEndedAsync(CreateRoundEndContext(sessionId, 2), msgs2, null);
        await Task.Delay(500);

        var finalMemory = await hook.GetSessionMemoryAsync();

        Assert.Contains("Build agent", finalMemory);
        Assert.Contains("Started", finalMemory);
    }

    [Fact]
    public async Task NoMemoryWritten_WhenLlmDisabled()
    {
        var sessionId = $"no-llm-{Guid.NewGuid():N}";
        var hook = new SessionMemoryHook(sessionId, TestUserId,
            options: new SessionMemoryOptions { EnableLlmSummary = false });
        var messages = new List<Message> { Message.FromUser("test message") };
        await hook.OnAgentRoundEndedAsync(CreateRoundEndContext(sessionId, 1), messages, null);
        await Task.Delay(300);

        var memory = await hook.GetSessionMemoryAsync();
        Assert.Equal("", memory);
    }

    private static InsightaAI.Agent.Context.Summary.ISummaryService CreateSummaryService(
        InsightaAI.LLM.Abstractions.ILlmClient client) =>
        new InsightaAI.Agent.Context.Summary.SummaryService(
            new InsightaAI.Agent.Context.Summary.SummaryOptions
            {
                Model = "mock/test-model",
                ClientFactory = _ => client
            });

    private static AgentEventHookContext CreateRoundEndContext(string sessionId, int round) =>
        AgentEventHookContext.Create(sessionId, new AgentRoundEndEvent
        {
            AgentId = "test-agent",
            Round = round
        });
}
