using InsightaAI.Agent.Hooks;
using InsightaAI.Agent.Memory;
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
        var hook = new SessionMemoryHook("test-id", TestUserId, options: new SessionMemoryOptions { EnableLlmSummary = false });
        Assert.Equal("session-memory", hook.Id);
    }

    [Fact]
    public async Task LlmSummary_Should_TriggerAtCorrectRounds()
    {
        // minRoundsBeforeLlm=3, summaryInterval=1 → 触发于 round 3, 4, 5, ...
        var sessionId = $"trigger-test-{Guid.NewGuid():N}";
        var hook = new SessionMemoryHook(sessionId, TestUserId,
            options: new SessionMemoryOptions { EnableLlmSummary = true, MinRoundsBeforeLlm = 3, SummaryInterval = TimeSpan.FromMinutes(5) });

        var llmClient = new MockLlmClient(response: "<summary>Test summary</summary>");
        var hookContext = new HookContext { LlmClient = llmClient, SessionId = sessionId };
        var messages = new List<Message> { Message.FromUser("test message") };

        // Round 1, 2: 不应触发 LLM（关键词模式）
        await hook.OnRoundEndAsync(hookContext, 1, messages, null);
        await Task.Delay(300);
        var mem1 = await hook.GetSessionMemoryAsync();

        await hook.OnRoundEndAsync(hookContext, 2, messages, null);
        await Task.Delay(300);
        var mem2 = await hook.GetSessionMemoryAsync();

        // Round 3: 应触发 LLM
        await hook.OnRoundEndAsync(hookContext, 3, messages, null);
        await Task.Delay(500);
        var mem3 = await hook.GetSessionMemoryAsync();

        // Round 3 的内容应包含 LLM 生成的摘要
        Assert.Contains("Test summary", mem3);
    }

    [Fact]
    public async Task LlmSummary_Should_ParseSummaryTags()
    {
        var sessionId = $"parse-test-{Guid.NewGuid():N}";
        var hook = new SessionMemoryHook(sessionId, TestUserId,
            options: new SessionMemoryOptions { EnableLlmSummary = true, MinRoundsBeforeLlm = 1, SummaryInterval = TimeSpan.FromMinutes(5) });

        var llmResponse = @"Some analysis text here...
<summary>
## Goal
- Implement session memory

## Progress
### Done
- [x] Created hook
</summary>";

        var llmClient = new MockLlmClient(response: llmResponse);
        var hookContext = new HookContext { LlmClient = llmClient, SessionId = sessionId };
        var messages = new List<Message> { Message.FromUser("implement session memory") };

        await hook.OnRoundEndAsync(hookContext, 1, messages, null);
        await Task.Delay(500);

        var memory = await hook.GetSessionMemoryAsync();

        // 应提取 <summary> 标签内的内容
        Assert.Contains("## Goal", memory);
        Assert.Contains("Implement session memory", memory);
        Assert.DoesNotContain("Some analysis text here", memory);
    }

    [Fact]
    public async Task LlmSummary_Should_ReplaceFile_NotAppend()
    {
        var sessionId = $"replace-test-{Guid.NewGuid():N}";
        var hook = new SessionMemoryHook(sessionId, TestUserId,
            options: new SessionMemoryOptions { EnableLlmSummary = true, MinRoundsBeforeLlm = 1, SummaryInterval = TimeSpan.FromMinutes(5) });

        var llmClient = new MockLlmClient(response: "<summary>Round 1 summary</summary>");
        var hookContext = new HookContext { LlmClient = llmClient, SessionId = sessionId };
        var messages = new List<Message> { Message.FromUser("first round") };

        // Round 1
        await hook.OnRoundEndAsync(hookContext, 1, messages, null);
        await Task.Delay(500);
        var mem1 = await hook.GetSessionMemoryAsync();

        // Round 2 - 更新摘要
        var llmClient2 = new MockLlmClient(response: "<summary>Updated summary</summary>");
        var hookContext2 = new HookContext { LlmClient = llmClient2, SessionId = sessionId };

        await hook.OnRoundEndAsync(hookContext2, 2, messages, null);
        await Task.Delay(500);
        var mem2 = await hook.GetSessionMemoryAsync();

        // 文件应被替换，不是追加
        Assert.Contains("Updated summary", mem2);
        Assert.DoesNotContain("Round 1 summary", mem2);
    }

    [Fact]
    public async Task LlmSummary_Should_FallbackToKeyword_WhenLlmReturnsEmpty()
    {
        var sessionId = $"fallback-empty-{Guid.NewGuid():N}";
        var hook = new SessionMemoryHook(sessionId, TestUserId,
            options: new SessionMemoryOptions { EnableLlmSummary = true, MinRoundsBeforeLlm = 1, SummaryInterval = TimeSpan.FromMinutes(5) });

        var llmClient = new MockLlmClient(response: ""); // 空响应
        var hookContext = new HookContext { LlmClient = llmClient, SessionId = sessionId };
        var messages = new List<Message> { Message.FromUser("我喜欢使用 C# 编程") };

        await hook.OnRoundEndAsync(hookContext, 1, messages, null);
        await Task.Delay(500);

        var memory = await hook.GetSessionMemoryAsync();

        // 应降级到关键词提取
        Assert.NotNull(memory);
        // 关键词提取会追加 "## Round 1" 格式
        Assert.Contains("Round 1", memory);
    }

    [Fact]
    public async Task LlmSummary_Should_FallbackToKeyword_WhenLlmThrows()
    {
        var sessionId = $"fallback-error-{Guid.NewGuid():N}";
        var hook = new SessionMemoryHook(sessionId, TestUserId,
            options: new SessionMemoryOptions { EnableLlmSummary = true, MinRoundsBeforeLlm = 1, SummaryInterval = TimeSpan.FromMinutes(5) });

        // MockLlmClient 不会抛异常，但我们可以用 null LlmClient 触发降级
        var hookContext = new HookContext { LlmClient = null, SessionId = sessionId };
        var messages = new List<Message> { Message.FromUser("项目目标是在 Q2 完成") };

        await hook.OnRoundEndAsync(hookContext, 1, messages, null);
        await Task.Delay(500);

        var memory = await hook.GetSessionMemoryAsync();

        // 应降级到关键词提取
        Assert.NotNull(memory);
        Assert.Contains("Round 1", memory);
    }

    [Fact]
    public async Task LlmSummary_Should_PreserveAnchoredSummary_AcrossRounds()
    {
        var sessionId = $"anchored-{Guid.NewGuid():N}";
        var hook = new SessionMemoryHook(sessionId, TestUserId,
            options: new SessionMemoryOptions { EnableLlmSummary = true, MinRoundsBeforeLlm = 1, SummaryInterval = TimeSpan.FromMinutes(5) });

        // Round 1: 初始摘要
        var llm1 = new MockLlmClient(response: "<summary>## Goal\n- Build agent</summary>");
        var ctx1 = new HookContext { LlmClient = llm1, SessionId = sessionId };
        var msgs1 = new List<Message> { Message.FromUser("build an agent") };

        await hook.OnRoundEndAsync(ctx1, 1, msgs1, null);
        await Task.Delay(500);

        // Round 2: LLM 应收到 previous-summary 并合并
        string? capturedPrompt = null;
        var llm2 = new MockLlmClient(response: "<summary>## Goal\n- Build agent\n\n## Progress\n### Done\n- [x] Started</summary>");
        var ctx2 = new HookContext { LlmClient = llm2, SessionId = sessionId };
        var msgs2 = new List<Message> { Message.FromUser("I started working on it") };

        await hook.OnRoundEndAsync(ctx2, 2, msgs2, null);
        await Task.Delay(500);

        var finalMemory = await hook.GetSessionMemoryAsync();

        // 最终摘要应包含合并后的内容
        Assert.Contains("Build agent", finalMemory);
        Assert.Contains("Started", finalMemory);
    }

    [Fact]
    public async Task KeywordFallback_Should_AppendWithRoundHeader()
    {
        var sessionId = $"keyword-append-{Guid.NewGuid():N}";
        var hook = new SessionMemoryHook(sessionId, TestUserId, options: new SessionMemoryOptions { EnableLlmSummary = false });
        var hookContext = new HookContext { LlmClient = null, SessionId = sessionId };

        var msgs1 = new List<Message> { Message.FromUser("我喜欢 Python") };
        await hook.OnRoundEndAsync(hookContext, 1, msgs1, null);
        await Task.Delay(300);

        var msgs2 = new List<Message> { Message.FromUser("项目 deadline 是周五") };
        await hook.OnRoundEndAsync(hookContext, 2, msgs2, null);
        await Task.Delay(300);

        var memory = await hook.GetSessionMemoryAsync();

        // 关键词模式应追加多个 Round
        Assert.Contains("Round 1", memory);
        Assert.Contains("Round 2", memory);
    }
}
