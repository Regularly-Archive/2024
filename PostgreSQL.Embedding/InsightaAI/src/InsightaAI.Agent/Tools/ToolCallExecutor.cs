using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Security;
using InsightaAI.Agent.Harness.Local;
using InsightaAI.LLM.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
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
    private readonly ISecretRedactor _redactor;
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
        _redactor = serviceProvider.GetService<ISecretRedactor>() ?? SecretRedactionPipeline.CreateDefault();
        _resultProcessor = serviceProvider.GetService<ToolResultProcessor>()
            ?? new ToolResultProcessor(toolRegistry, artifactStore, _redactor);
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
        await foreach (var evt in ExecuteToolsParallelCoreAsync(toolCalls, cancellationToken))
            yield return evt;
    }

    public async IAsyncEnumerable<AgentEvent> ExecuteToolsSequentialAsync(
        ToolCallBlock[] toolCalls,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var evt in ExecuteToolsSequentialCoreAsync(toolCalls, cancellationToken))
            yield return evt;
    }

    private async IAsyncEnumerable<AgentEvent> ExecuteToolsParallelCoreAsync(
        ToolCallBlock[] toolCalls,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var toolEvents = CreateToolEventsChannel();
        var toolResults = new ConcurrentDictionary<string, ToolExecutionResult>();

        var producer = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(toolCalls.Select(toolCall => RunToolAsync(
                    toolCall, toolEvents.Writer, toolResults, cancellationToken)));
                toolEvents.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                toolEvents.Writer.TryComplete(exception);
            }
        });

        await foreach (var evt in toolEvents.Reader.ReadAllAsync(cancellationToken))
            yield return evt;

        await producer;
        Results = toolCalls.Select(toolCall => toolResults[toolCall.Id]).ToArray();
    }

    private async IAsyncEnumerable<AgentEvent> ExecuteToolsSequentialCoreAsync(
        ToolCallBlock[] toolCalls,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var toolResults = new ConcurrentDictionary<string, ToolExecutionResult>();

        foreach (var toolCall in toolCalls)
        {
            var toolEvents = CreateToolEventsChannel();
            var execution = Task.Run(async () =>
            {
                try
                {
                    await RunToolAsync(toolCall, toolEvents.Writer, toolResults, cancellationToken);
                    toolEvents.Writer.TryComplete();
                }
                catch (Exception exception)
                {
                    toolEvents.Writer.TryComplete(exception);
                }
            });

            // The iterator pauses at each yield until the consumer asks for the next event.
            // Consequently the next Tool Call cannot begin until the consumer has processed this
            // tool's ToolEnd event and advances the stream past this per-tool channel.
            await foreach (var evt in toolEvents.Reader.ReadAllAsync(cancellationToken))
                yield return evt;

            await execution;
        }

        Results = toolCalls.Select(toolCall => toolResults[toolCall.Id]).ToArray();
    }

    private static Channel<AgentEvent> CreateToolEventsChannel() =>
        Channel.CreateBounded<AgentEvent>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

    private async Task RunToolAsync(
        ToolCallBlock toolCall,
        ChannelWriter<AgentEvent> toolEvents,
        ConcurrentDictionary<string, ToolExecutionResult> toolResults,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var arguments = toolCall.Arguments.GetRawText();
        await toolEvents.WriteAsync(new AgentToolStartEvent
        {
            AgentId = _agentId,
            ToolCallId = toolCall.Id,
            ToolName = toolCall.Name,
            Arguments = arguments
        }, cancellationToken);

        var request = new ToolCallRequest
        {
            ToolCall = toolCall,
            SessionId = _sessionId,
            Arguments = arguments,
            Progress = new ToolCallProgressReporter(_agentId, toolCall, toolEvents, _redactor)
        };
        var response = await _handler.Invoke(request, cancellationToken);
        var processed = await _resultProcessor.ProcessAsync(
            _sessionId, toolCall, response.ToolResult, _enableInterception, cancellationToken);
        var resultText = processed.Result.Content.OfType<TextBlock>().FirstOrDefault()?.Text;
        await toolEvents.WriteAsync(new AgentToolEndEvent
        {
            AgentId = _agentId,
            ToolCallId = toolCall.Id,
            ToolName = toolCall.Name,
            IsError = processed.Result.IsError,
            ResultPreview = resultText?.Length > 100 ? resultText[..100] + "..." : resultText
        }, cancellationToken);

        toolResults[toolCall.Id] = new ToolExecutionResult(toolCall, processed.Result, processed.State);
    }

    private sealed class ToolCallProgressReporter : IToolProgressReporter
    {
        private readonly string _agentId;
        private readonly ToolCallBlock _toolCall;
        private readonly ChannelWriter<AgentEvent> _writer;
        private readonly ISecretRedactor _redactor;

        public ToolCallProgressReporter(
            string agentId,
            ToolCallBlock toolCall,
            ChannelWriter<AgentEvent> writer,
            ISecretRedactor redactor)
        {
            _agentId = agentId;
            _toolCall = toolCall;
            _writer = writer;
            _redactor = redactor;
        }

        public ValueTask ReportAsync(ToolProgressUpdate update, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(update);
            var context = new RedactionContext
            {
                ToolName = _toolCall.Name,
                Format = SecretContentFormat.PlainText
            };
            var progress = update with
            {
                Message = update.Message is null ? null : _redactor.Redact(update.Message, context).Content,
                Text = update.Text is null ? null : _redactor.Redact(update.Text, context).Content
            };
            _writer.TryWrite(new AgentToolProgressEvent
            {
                AgentId = _agentId,
                ToolCallId = _toolCall.Id,
                ToolName = _toolCall.Name,
                Progress = progress
            });
            return ValueTask.CompletedTask;
        }
    }

}

#region
public record ToolCallRequest
{
    public ToolCallBlock ToolCall { get; set; }
    public string Arguments { get; set; }
    public string SessionId { get; set; }
    public IToolProgressReporter Progress { get; set; } = NullToolProgressReporter.Instance;
}

public record ToolCallResponse(bool IsAllowed, ToolResult ToolResult);

public delegate Task<ToolCallResponse> ToolCallHandler(ToolCallRequest request, CancellationToken cancellationToken);
#endregion
