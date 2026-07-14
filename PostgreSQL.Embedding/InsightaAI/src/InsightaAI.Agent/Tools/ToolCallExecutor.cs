using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace InsightaAI.Agent.Tools;

/// <summary>
/// 工具执行结果
/// </summary>
public sealed record ToolExecutionResult(
    ToolCallBlock ToolCall,
    ToolResult Result,
    bool Intercepted);

public class ToolCallExecutor
{
    private readonly string _agentId;
    private readonly string _sessionId;
    private readonly ToolCallHandler _handler;
    private readonly ToolRegistry? _toolRegistry;
    private readonly IServiceProvider _serviceProvider;
    private readonly bool _enableInterception;
    private const int LARGE_TOOL_RESULT_THRESHOLD = 30 * 1024;
    private readonly string _basePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".insighta",
        "sessions"
    );

    public ToolCallExecutor(string agentId, string sessionId, ToolCallHandler handler,
        IServiceProvider serviceProvider, bool enableInterception = true)
    {
        _agentId = agentId;
        _sessionId = sessionId;
        _handler = handler;
        _toolRegistry = serviceProvider.GetRequiredService<ToolRegistry>();
        _serviceProvider = serviceProvider;
        _enableInterception = enableInterception;
    }

    /// <summary>
    /// 最近一次执行的工具结果（在 ExecuteTools*Async 完成后可用）
    /// </summary>
    public IReadOnlyList<ToolExecutionResult> Results { get; private set; } = [];

    /// <summary>
    /// 拦截工具结果（如果启用且工具实现了 Intercept）
    /// </summary>
    private async Task<(ToolResult Result, bool Intercepted)> TryInterceptResultAsync(
        string toolName, string toolCallId, ToolResult toolResult, CancellationToken cancellationToken)
    {
        if (!_enableInterception || _toolRegistry == null)
            return (toolResult, false);

        var executor = _toolRegistry.GetExecutor(toolName);
        if (executor == null)
            return (toolResult, false);

        // 创建工具结果截取上下文
        var truncationContext = CreateTruncationContext(toolName, toolCallId, toolResult);

        // 如果工具实现了 Intercept 接口，则使用工具的拦截策略，否则应用默认的拦截策略
        var interceptedResult = executor.Intercept(toolResult, truncationContext);
        if (interceptedResult.ToolResultIntercepted)
            return (interceptedResult.Result, interceptedResult.ToolResultIntercepted);

        return await ApplyDefaultTruncationPolicy(toolResult, truncationContext, cancellationToken);
    }

    public async IAsyncEnumerable<AgentEvent> ExecuteToolsParallelAsync(
        ToolCallBlock[] toolCalls,
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

        // 拦截结果并存储（不再直接操作 messages）
        var finalResults = new List<ToolExecutionResult>();
        foreach (var (toolCall, toolResult) in toolResults)
        {
            var (finalResult, intercepted) = await TryInterceptResultAsync(toolCall.Name, toolCall.Id, toolResult, cancellationToken);
            finalResults.Add(new ToolExecutionResult(toolCall, finalResult, intercepted));
        }
        Results = finalResults;
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

            // 拦截结果并存储
            var (finalResult, intercepted) = await TryInterceptResultAsync(toolCall.Name, toolCall.Id, toolResult, cancellationToken);
            finalResults.Add(new ToolExecutionResult(toolCall, finalResult, intercepted));
        }

        Results = finalResults;
    }

    private TruncationContext CreateTruncationContext(string toolName, string toolCallId, ToolResult toolResult)
    {
        // 计算结果大小
        var textBlocks = toolResult.Content.OfType<TextBlock>().ToList();
        var totalText = string.Join("\n", textBlocks.Select(t => t.Text));

        var contextManager = _serviceProvider.GetService<IContextManager>();
        var truncationContext = new TruncationContext(
            originalLength: totalText.Length,
            originalLineCount: new Lazy<int>(() => totalText.Split('\n').Length),
            utilizationRatio: 0,
            budget: contextManager?.GetContextBudget() ?? new ContextBudget(),
            toolResultDirectory: Path.Combine(_basePath, _sessionId, "tool_results"),
            toolName: toolName,
            toolCallId: toolCallId
        );

        return truncationContext;
    }

    private async Task<(ToolResult Result, bool Intercepted)> ApplyDefaultTruncationPolicy(ToolResult toolResult, TruncationContext truncationContext, CancellationToken cancellationToken)
    {
        var textBlocks = toolResult.Content.OfType<TextBlock>().ToList();
        var totalText = string.Join("\n", textBlocks.Select(t => t.Text));

        var byteSize = Encoding.UTF8.GetByteCount(totalText);
        if (byteSize <= LARGE_TOOL_RESULT_THRESHOLD) return (toolResult, false);

        var filePath = Path.Combine(truncationContext.ToolResultDirectory, $"{DateTime.Now:yyyyMMdd_HHmmss}_{truncationContext.ToolName}.txt");
        var sizeKB = Math.Round(byteSize / 1024M, 1);
        var preview = string.Join("\n", totalText.Split("\n").Take(200));

        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine($"[The result is too large (${sizeKB} KB, ${truncationContext.OriginalLineCount} lines). Full output saved to ${filePath}. You can use read_file to see the full result.]");
        stringBuilder.AppendLine("Preview (first 200 lines):");
        stringBuilder.AppendLine(preview);

        var fileSystem = _serviceProvider.GetRequiredService<IFileSystem>();
        await fileSystem.WriteFileAsync(filePath, stringBuilder.ToString(), Encoding.UTF8, cancellationToken);

        var truncatedToolResult = new ToolResult() { Content = [new TextBlock() { Text = stringBuilder.ToString() }], IsError = false };
        return (truncatedToolResult, true);
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
