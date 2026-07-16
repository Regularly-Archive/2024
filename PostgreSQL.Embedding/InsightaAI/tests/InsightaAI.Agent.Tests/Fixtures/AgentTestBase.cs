using System.Text;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tests.Fixtures;

/// <summary>
/// Agent 测试基类 - 提供通用的辅助方法
/// </summary>
public abstract class AgentTestBase : IClassFixture<AgentFixture>
{
    protected readonly AgentFixture Fixture;

    protected AgentTestBase(AgentFixture fixture)
    {
        Fixture = fixture;
    }

    /// <summary>
    /// 运行 Agent 并捕获流式输出结果
    /// </summary>
    protected async Task<string> RunAgentAndCaptureResultAsync(Agent agent, string input)
    {
        var result = new StringBuilder();

        await foreach (var evt in agent.RunStreamAsync(input))
        {
            if (evt is AgentLlmStreamEvent llmEvent &&
                llmEvent.StreamEvent is TextDeltaEvent textDelta)
            {
                result.Append(textDelta.Delta);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// 运行 Agent 并收集所有事件
    /// </summary>
    protected async Task<List<AgentEvent>> RunAgentAndCollectEventsAsync(Agent agent, string input)
    {
        var events = new List<AgentEvent>();

        await foreach (var evt in agent.RunStreamAsync(input))
        {
            events.Add(evt);
        }

        return events;
    }

    /// <summary>
    /// 运行 Agent 并返回完整结果
    /// </summary>
    protected async Task<AgentResult> RunAgentAndGetResultAsync(Agent agent, string input)
    {
        AgentResult? result = null;

        await foreach (var evt in agent.RunStreamAsync(input))
        {
            if (evt is AgentSessionEndEvent completeEvent)
            {
                result = completeEvent.Result;
            }
        }

        return result ?? throw new InvalidOperationException("Agent did not complete");
    }

    /// <summary>
    /// 打印流式事件（调试用）
    /// </summary>
    protected static async Task PrintStreamEventsAsync(Agent agent, string input)
    {
        await foreach (var evt in agent.RunStreamAsync(input))
        {
            switch (evt)
            {
                case AgentSessionStartEvent start:
                    Console.WriteLine($"[AgentStart] Agent: {start.AgentName}, Model: {start.Model}");
                    break;

                case AgentRoundStartEvent roundStart:
                    Console.WriteLine($"\n--- Round {roundStart.Round} ---");
                    break;

                case AgentLlmStreamEvent llmEvent:
                    if (llmEvent.StreamEvent is TextDeltaEvent textDelta)
                    {
                        Console.Write(textDelta.Delta);
                    }
                    else if (llmEvent.StreamEvent is ThinkingDeltaEvent thinkingDelta)
                    {
                        Console.Write($"[Thinking] {thinkingDelta.Delta}");
                    }
                    break;

                case AgentToolStartEvent toolStart:
                    Console.WriteLine($"\n>> Calling: {toolStart.ToolName} (Id: {toolStart.ToolCallId})");
                    break;

                case AgentToolEndEvent toolEnd:
                    Console.WriteLine($"<< Result: {toolEnd.ResultPreview}");
                    break;

                case AgentSessionEndEvent complete:
                    Console.WriteLine($"\n[Complete] Rounds: {complete.Result.Rounds}, Duration: {complete.Result.DurationMs}ms");
                    break;
            }
        }
    }

    /// <summary>
    /// 断言事件中包含特定工具调用
    /// </summary>
    protected static void AssertContainsToolCall(List<AgentEvent> events, string toolName)
    {
        var toolStartEvents = events.OfType<AgentToolStartEvent>().ToList();
        Assert.Contains(toolStartEvents, e => e.ToolName == toolName);
    }

    /// <summary>
    /// 断言 Agent 完成状态
    /// </summary>
    protected static void AssertAgentCompleted(List<AgentEvent> events, int? expectedRounds = null)
    {
        var completeEvent = events.OfType<AgentSessionEndEvent>().FirstOrDefault();
        Assert.NotNull(completeEvent);
        Assert.Equal(AgentStatus.Completed, completeEvent.Result.Status);

        if (expectedRounds.HasValue)
        {
            Assert.Equal(expectedRounds.Value, completeEvent.Result.Rounds);
        }
    }
}
