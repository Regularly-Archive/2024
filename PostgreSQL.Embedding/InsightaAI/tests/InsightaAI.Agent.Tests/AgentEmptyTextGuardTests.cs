using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tests;

/// <summary>
/// 空文本守卫测试：模型以纯 thinking（或空内容）结束回复且无工具调用时，
/// AgentLoop 不应宣告完成——剩余轮次内继续循环让模型补写正文；
/// 已到最大轮次则落入 max-rounds 收尾强制生成结论。
/// 守卫仅记 Warning 日志，不产生用户可见的错误事件。
/// </summary>
public sealed class AgentEmptyTextGuardTests
{
    [Fact]
    public async Task ThinkingOnlyFinal_RetriesNextRound_AndCompletesWithText()
    {
        var client = new ScriptedLlmClient(
            ThinkingOnlyResponse(),
            TextResponse("Recovered answer."));
        using var agent = new Agent(CreateConfig(maxToolRounds: 3), client, new ToolRegistry());

        var events = new List<AgentEvent>();
        await foreach (var @event in agent.RunStreamAsync("Do the analysis."))
            events.Add(@event);

        Assert.Equal(2, client.CallCount);
        Assert.Empty(events.OfType<AgentErrorEvent>());

        var turnEnd = Assert.Single(events.OfType<AgentTurnEndEvent>());
        Assert.Equal(AgentStatus.Completed, turnEnd.Result.Status);
        Assert.Equal(2, turnEnd.Result.Rounds);
        Assert.Equal("Recovered answer.", turnEnd.Result.Message!.GetTextContent());
    }

    [Fact]
    public async Task ThinkingOnlyFinal_OnLastRound_FallsBackToMaxRoundsSummary()
    {
        var client = new ScriptedLlmClient(
            ThinkingOnlyResponse(),
            TextResponse("Summary answer."));
        using var agent = new Agent(CreateConfig(maxToolRounds: 1), client, new ToolRegistry());

        var events = new List<AgentEvent>();
        await foreach (var @event in agent.RunStreamAsync("Do the analysis."))
            events.Add(@event);

        Assert.Equal(2, client.CallCount);

        // 第二次调用应来自 HandleMaxRoundsExceededAsync：无工具、强制收尾
        var finalRequest = client.Requests[1];
        Assert.Empty(finalRequest.Tools ?? []);
        Assert.Equal(ToolChoiceMode.None, finalRequest.ToolChoice);

        Assert.Empty(events.OfType<AgentErrorEvent>());

        var turnEnd = Assert.Single(events.OfType<AgentTurnEndEvent>());
        Assert.Equal(AgentStatus.Completed, turnEnd.Result.Status);
        Assert.Equal("Summary answer.", turnEnd.Result.Message!.GetTextContent());
    }

    private static AgentConfig CreateConfig(int maxToolRounds) => new()
    {
        Id = "guard-agent",
        Name = "Guard Agent",
        Model = "test-model",
        MaxToolRounds = maxToolRounds
    };

    private static LlmResponse TextResponse(string text) => new()
    {
        Model = "test-model",
        Content = [new TextBlock { Text = text }],
        FinishReason = DoneReason.Complete
    };

    private static LlmResponse ThinkingOnlyResponse() => new()
    {
        Model = "test-model",
        Content = [new ThinkingBlock { Thinking = "long reasoning without a final answer..." }],
        FinishReason = DoneReason.Complete
    };

    private sealed class ScriptedLlmClient(params LlmResponse[] responses) : ILlmClient
    {
        private int _call;

        public List<LlmRequest> Requests { get; } = [];

        public int CallCount => _call;

        public string AdapterName => "scripted-test";

        public bool SupportsReasoning => false;

        public LlmStream Streaming(LlmRequest request)
        {
            var response = responses[_call++];
            Requests.Add(request);
            return new ScriptedLlmStream(response);
        }

        public Task<LlmResponse> CompleteAsync(LlmRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class ScriptedLlmStream(LlmResponse response) : LlmStream
    {
        public bool IsCompleted { get; private set; }
        public bool IsAborted { get; private set; }

        public async IAsyncEnumerator<StreamEvent> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new DoneEvent { Reason = response.FinishReason };
            IsCompleted = true;
        }

        public Task<LlmResponse> GetResponseAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(response);

        public void Abort()
        {
        }

        public void Dispose()
        {
        }
    }
}
