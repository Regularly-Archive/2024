using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Security;
using InsightaAI.Agent.Harness.Local;
using InsightaAI.LLM.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace InsightaAI.Agent.Tools;

/// <summary>
/// 工具执行结果
/// </summary>
public sealed record ToolExecutionResult(
    ToolCallBlock ToolCall,
    ToolResult Result,
    ToolResultState State);

public class ToolCallExecutor
{
    private readonly string _agentId;
    private readonly string _sessionId;
    private readonly ToolCallHandler _handler;
    private readonly ToolResultProcessor _resultProcessor;
    private readonly bool _enableInterception;

    public ToolCallExecutor(string agentId, string sessionId, ToolCallHandler handler,
        IServiceProvider serviceProvider, bool enableInterception = true)
    {
        _agentId = agentId;
        _sessionId = sessionId;
        _handler = handler;
        var toolRegistry = serviceProvider.GetRequiredService<ToolRegistry>();
        var fileSystem = serviceProvider.GetService<IFileSystem>() ?? new LocalFileSystem();
        var artifactStore = serviceProvider.GetService<IToolResultArtifactStore>()
            ?? new ToolResultArtifactStore(fileSystem);
        var redactor = serviceProvider.GetService<ISecretRedactor>() ?? SecretRedactionPipeline.CreateDefault();
        _resultProcessor = serviceProvider.GetService<ToolResultProcessor>()
            ?? new ToolResultProcessor(toolRegistry, artifactStore, redactor);
        _enableInterception = enableInterception;
    }

    /// <summary>
    /// 最近一次执行的工具结果（在 ExecuteTools*Async 完成后可用）
    /// </summary>
    public IReadOnlyList<ToolExecutionResult> Results { get; private set; } = [];

    public async IAsyncEnumerable<AgentEvent> ExecuteToolsParallelAsync(
        ToolCallBlock[] toolCalls,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var toolEvents = Channel.CreateUnbounded<AgentEvent>();
        var toolResults = new Dictionary<string, ToolExecutionResult>();
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
            var processed = await _resultProcessor.ProcessAsync(
                _sessionId, toolCall, toolCallResponse.ToolResult, _enableInterception, cancellationToken);

            // 发送工具完成事件
            var resultText = processed.Result.Content.OfType<TextBlock>().FirstOrDefault()?.Text;
            await toolEvents.Writer.WriteAsync(new AgentToolEndEvent
            {
                AgentId = _agentId,
                ToolCallId = toolCall.Id,
                ToolName = toolCall.Name,
                IsError = processed.Result.IsError,
                ResultPreview = resultText?.Length > 100 ? resultText[..100] + "..." : resultText
            }, cancellationToken);

            // 收集结果
            lock (toolResultsLock)
            {
                toolResults[toolCall.Id] = new ToolExecutionResult(toolCall, processed.Result, processed.State);
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

        Results = toolCalls.Select(toolCall => toolResults[toolCall.Id]).ToArray();
    }

    public async IAsyncEnumerable<AgentEvent> ExecuteToolsSequentialAsync(
        ToolCallBlock[] toolCalls,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var finalResults = new List<ToolExecutionResult>();

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
            var processed = await _resultProcessor.ProcessAsync(
                _sessionId, toolCall, toolCallResponse.ToolResult, _enableInterception, cancellationToken);

            // 发送工具完成事件
            var resultText = processed.Result.Content.OfType<TextBlock>().FirstOrDefault()?.Text;
            yield return new AgentToolEndEvent
            {
                AgentId = _agentId,
                ToolCallId = toolCall.Id,
                ToolName = toolCall.Name,
                IsError = processed.Result.IsError,
                ResultPreview = resultText?.Length > 100 ? resultText[..100] + "..." : resultText
            };

            finalResults.Add(new ToolExecutionResult(toolCall, processed.Result, processed.State));
        }

        Results = finalResults;
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
