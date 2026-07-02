using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Models;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace InsightaAI.Agent.Tools;

public class ToolExecutor
{
    private readonly string _agentId;
    private readonly string _sessionId;
    private readonly ToolCallHandler _handler;

    public ToolExecutor(string agentId, string sessionId, ToolCallHandler handler)
    {
        _agentId = agentId;
        _sessionId = sessionId;
        _handler = handler;
    }

    public async IAsyncEnumerable<AgentEvent> ExecuteToolsParallelAsync(
        ToolCallBlock[] toolCalls,
        List<Message> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var toolEvents = Channel.CreateUnbounded<AgentEvent>();
        var toolResults = new List<(ToolCallBlock ToolCall, ToolResult Result)>();
        var toolResultsLock = new object();

        var tasks = toolCalls.Select(async toolCall =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var arguments = toolCall.Arguments.GetRawText();

            // 发送工具开始事件
            await toolEvents.Writer.WriteAsync(new AgentToolStartEvent
            {
                AgentId = _agentId,
                ToolCallId = toolCall.Id,
                ToolName = toolCall.Name,
                Arguments = arguments
            }, cancellationToken);

            var toolCallRequest = new ToolCallRequest() { ToolCall = toolCall, SessionId = _sessionId, Arguments = arguments };
            var toolCallResponse = await _handler.Invoke(toolCallRequest, cancellationToken);
            var toolResult = toolCallResponse.ToolResult;

            // 发送工具完成事件
            var resultText = toolResult.Content.OfType<TextBlock>().FirstOrDefault()?.Text;
            await toolEvents.Writer.WriteAsync(new AgentToolEndEvent
            {
                AgentId = _agentId,
                ToolCallId = toolCall.Id,
                ToolName = toolCall.Name,
                IsError = toolResult.IsError,
                ResultPreview = resultText?.Length > 100 ? resultText[..100] + "..." : resultText
            }, cancellationToken);

            // 收集结果
            lock (toolResultsLock)
            {
                toolResults.Add((toolCall, toolResult));
            }
        }).ToArray();

        // 关闭 channel 当所有任务完成时
        _ = Task.WhenAll(tasks).ContinueWith(_ => toolEvents.Writer.Complete());

        // 转发事件
        await foreach (var evt in toolEvents.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }

        // 等待所有任务完成
        await Task.WhenAll(tasks);

        // 按原始顺序添加工具结果到对话历史
        foreach (var (toolCall, toolResult) in toolResults)
        {
            messages.Add(new Message
            {
                Role = MessageRole.ToolResult,
                ToolCallId = toolCall.Id,
                ToolName = toolCall.Name,
                Content = toolResult.Content
            });
        }
    }

    public async IAsyncEnumerable<AgentEvent> ExecuteToolsSequentialAsync(
        ToolCallBlock[] toolCalls,
        List<Message> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var toolCall in toolCalls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var arguments = toolCall.Arguments.GetRawText();

            // 发送工具开始事件
            yield return new AgentToolStartEvent
            {
                AgentId = _agentId,
                ToolCallId = toolCall.Id,
                ToolName = toolCall.Name,
                Arguments = arguments
            };

            var toolCallRequest = new ToolCallRequest() { ToolCall = toolCall, SessionId = _sessionId, Arguments = arguments };
            var toolCallResponse = await _handler.Invoke(toolCallRequest, cancellationToken);
            var toolResult = toolCallResponse.ToolResult;

            // 发送工具完成事件
            var resultText = toolResult.Content.OfType<TextBlock>().FirstOrDefault()?.Text;
            yield return new AgentToolEndEvent
            {
                AgentId = _agentId,
                ToolCallId = toolCall.Id,
                ToolName = toolCall.Name,
                IsError = toolResult.IsError,
                ResultPreview = resultText?.Length > 100 ? resultText[..100] + "..." : resultText
            };

            // 将工具结果加入对话历史
            messages.Add(new Message
            {
                Role = MessageRole.ToolResult,
                ToolCallId = toolCall.Id,
                ToolName = toolCall.Name,
                Content = toolResult.Content
            });
        }
    }
}

#region 
public record ToolCallRequest
{
    public ToolCallBlock ToolCall { get; set; }
    public string Arguments { get; set; }
    public string SessionId { get; set; }
}

public record ToolCallReponse(bool IsAllowed, ToolResult ToolResult);

public delegate Task<ToolCallReponse> ToolCallHandler(ToolCallRequest request, CancellationToken cancellationToken);
#endregion
