using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Storage;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace InsightaAI.Agent.Tests;

/// <summary>
/// 集成测试：文本收尾管线（截断续写 + 空文本指令重试 + no-progress 止损）。
/// 锁定链路：截断文本在请求级投影上累积续写、Result.Message 合并为单条助手消息、
/// 用尽续写上限后标记 WasTruncated、空文本注入指令且不入历史、连续 no-progress 止损、
/// 开关关闭时保持既有行为。
/// </summary>
public sealed class AgentLoopContinuationTests
{
    private static async Task<(List<AgentEvent> Events, MockLlmClient Mock, AgentTurnEndEvent TurnEnd)> RunAgentAsync(
        MockLlmClient mock, bool enableOutputContinuation = true)
    {
        var storageDir = Path.Combine(Path.GetTempPath(), $"insighta-cont-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageDir);

        var services = new ServiceCollection();
        services.AddSingleton<ILlmClient>(mock);
        services.AddSingleton(new ToolRegistry());
        services.AddSingleton<IMessageStorage>(new JsonlMessageStorage(storageDir));
        using var serviceProvider = services.BuildServiceProvider();

        var config = new AgentConfig
        {
            Id = "continuation-agent",
            Name = "Continuation Agent",
            CustomInstructions = "You write long reports.",
            Model = "test-model",
            MaxToolRounds = 5,
            EnableOutputContinuation = enableOutputContinuation
        };
        var agent = new Agent(config, serviceProvider);
        var storage = serviceProvider.GetRequiredService<IMessageStorage>();
        var session = await storage.CreateSessionAsync("test-model", "mock", workDir: storageDir);

        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStreamAsync(
            "Write the annual report.",
            new AgentContext { SessionId = session.Id }))
        {
            events.Add(evt);
        }

        return (events, mock, events.OfType<AgentTurnEndEvent>().Single());
    }

    private static string TextOf(Message message) =>
        string.Concat(message.Content.OfType<TextBlock>().Select(b => b.Text));

    [Fact]
    public async Task TruncatedFirstTurn_Should_Continue_And_MergeIntoSingleMessage()
    {
        var mock = new MockLlmClient(
            response: "The report begins.",
            firstFinishReason: DoneReason.MaxTokens,
            secondResponse: " and it continues here.",
            secondFinishReason: DoneReason.Complete);

        var (events, mockClient, turnEnd) = await RunAgentAsync(mock);

        // 续写后正常结束，不标记截断
        Assert.False(turnEnd.Result!.WasTruncated);
        Assert.Equal(AgentStatus.Completed, turnEnd.Result.Status);

        // 历史视角只有一条助手消息：累积正文合并
        Assert.Equal("The report begins. and it continues here.", TextOf(turnEnd.Result.Message));

        // 续写流事件已转发给消费者
        Assert.Contains(events, e => e is AgentLlmStreamEvent);

        // 首轮 + 1 次续写
        Assert.Equal(2, mockClient.Requests.Count);

        // 续写请求是请求级投影：历史 + assistant 草稿 + user 续写指令，且不带工具
        var continuationRequest = mockClient.Requests[1];
        Assert.Equal(0, continuationRequest.Tools!.Length);
        var draftMessage = continuationRequest.Messages[^2];
        Assert.Equal(MessageRole.Assistant, draftMessage.Role);
        Assert.Equal("The report begins.", TextOf(draftMessage));
        var promptMessage = continuationRequest.Messages[^1];
        Assert.Equal(MessageRole.User, promptMessage.Role);

        // 用量累计两轮（Mock 每轮固定 input 10 / output 20）
        Assert.Equal(20, turnEnd.Result.Usage.InputTokens);
        Assert.Equal(40, turnEnd.Result.Usage.OutputTokens);
    }

    [Fact]
    public async Task TruncationExhaustingLimit_Should_MarkWasTruncated_And_StillMerge()
    {
        // 每轮都撞输出上限 → 1 次首轮 + MaxContinuations 次续写
        var mock = new MockLlmClient(
            response: "chunk-",
            firstFinishReason: DoneReason.MaxTokens,
            secondResponse: "chunk-",
            secondFinishReason: DoneReason.MaxTokens);

        var (_, mockClient, turnEnd) = await RunAgentAsync(mock);

        // 用尽上限后仍标记截断
        Assert.True(turnEnd.Result!.WasTruncated);
        Assert.Equal(AgentStatus.Completed, turnEnd.Result.Status);

        // 无论续写多少轮，最终仍是合并后的单条助手消息
        var expectedChunks = 1 + 16;
        Assert.Equal(new string('c', 0) + string.Concat(Enumerable.Repeat("chunk-", expectedChunks)),
            TextOf(turnEnd.Result.Message));
        Assert.Equal(expectedChunks, mockClient.Requests.Count);
    }

    [Fact]
    public async Task EmptyTextWithMaxTokens_Should_InjectInstruction_And_Recover()
    {
        // 首轮纯 thinking 空响应（推理耗尽输出预算）→ 指令轮直接输出正文
        var mock = new MockLlmClient(
            response: "",
            firstFinishReason: DoneReason.MaxTokens,
            secondResponse: "Recovered answer.",
            secondFinishReason: DoneReason.Complete);

        var (events, mockClient, turnEnd) = await RunAgentAsync(mock);

        Assert.Equal(AgentStatus.Completed, turnEnd.Result!.Status);
        Assert.False(turnEnd.Result.WasTruncated);
        Assert.Equal("Recovered answer.", TextOf(turnEnd.Result.Message));

        // 首轮 + 1 次指令轮
        Assert.Equal(2, mockClient.Requests.Count);

        // 指令轮投影：无 assistant 前置块（无正文可续），user 消息携带空文本指令
        var retryRequest = mockClient.Requests[1];
        Assert.Empty(retryRequest.Tools!);
        var lastMessage = retryRequest.Messages[^1];
        Assert.Equal(MessageRole.User, lastMessage.Role);
        Assert.Contains("no visible content", TextOf(lastMessage), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(retryRequest.Messages, m =>
            m.Role == MessageRole.Assistant && m.Content.Length == 0);

        // 流事件已转发
        Assert.Contains(events, e => e is AgentLlmStreamEvent);
    }

    [Fact]
    public async Task EmptyTextRetries_Should_Stop_AfterNoProgressLimit()
    {
        // 首轮空 + 指令轮仍空 + 再指令轮仍空 → 连续 no-progress 达上限止损
        var mock = new MockLlmClient(
            response: "",
            firstFinishReason: DoneReason.MaxTokens,
            secondResponse: "",
            secondFinishReason: DoneReason.MaxTokens);

        var (_, mockClient, turnEnd) = await RunAgentAsync(mock);

        Assert.Equal(AgentStatus.Completed, turnEnd.Result!.Status);
        // 全空不算截断交付
        Assert.False(turnEnd.Result.WasTruncated);
        // 首轮 + 2 次 no-progress 迭代（上限）
        Assert.Equal(3, mockClient.Requests.Count);
        // 消息原样入历史（无正文可合并）
        Assert.Equal("", TextOf(turnEnd.Result.Message!));
    }

    [Fact]
    public async Task ContinuationZeroProgress_Should_StopLoss_WhenContinuationsTurnEmpty()
    {
        // 首轮正文截断 → 续写轮连续两轮空转 → no-progress 止损，保留已累积正文
        var mock = new MockLlmClient(
            response: "partial report.",
            firstFinishReason: DoneReason.MaxTokens,
            secondResponse: "",
            secondFinishReason: DoneReason.MaxTokens);

        var (_, mockClient, turnEnd) = await RunAgentAsync(mock);

        Assert.Equal(AgentStatus.Completed, turnEnd.Result!.Status);
        // 止损时草稿无正文增量，不算截断交付
        Assert.False(turnEnd.Result.WasTruncated);
        // 首轮 + 2 次 no-progress 续写迭代
        Assert.Equal(3, mockClient.Requests.Count);
        // 已累积正文保留为单条合并消息
        Assert.Equal("partial report.", TextOf(turnEnd.Result.Message));
    }

    [Fact]
    public async Task ContinuationDisabled_Should_KeepLegacyBehavior()
    {
        // 开关关闭（默认）：截断仅标记 WasTruncated，不续写
        var mock = new MockLlmClient(
            response: "truncated answer.",
            firstFinishReason: DoneReason.MaxTokens,
            secondResponse: " never used.",
            secondFinishReason: DoneReason.Complete);

        var (_, mockClient, turnEnd) = await RunAgentAsync(mock, enableOutputContinuation: false);

        Assert.Equal(AgentStatus.Completed, turnEnd.Result!.Status);
        Assert.True(turnEnd.Result.WasTruncated);
        Assert.Equal("truncated answer.", TextOf(turnEnd.Result.Message));

        // 只有首轮请求，无收尾迭代
        Assert.Single(mockClient.Requests);
    }
}
