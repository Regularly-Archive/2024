using System.Text;
using System.Text.Json;
using InsightaAI.LLM.Models;

namespace InsightaAI.LLM.Abstractions;

/// <summary>
/// LLM 流式响应接口
/// </summary>
public interface LlmStream : IAsyncEnumerable<StreamEvent>, IDisposable
{
    /// <summary>
    /// 等待流完成并返回最终响应
    /// </summary>
    Task<LlmResponse> GetResponseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 中断流
    /// </summary>
    void Abort();

    /// <summary>
    /// 流是否已完成
    /// </summary>
    bool IsCompleted { get; }

    /// <summary>
    /// 流是否被中止
    /// </summary>
    bool IsAborted { get; }
}

/// <summary>
/// LlmStream 的基础实现
/// </summary>
public class LlmStreamImpl : LlmStream
{
    private readonly IAsyncEnumerable<StreamEvent> _events;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<StreamEvent> _cachedEvents = new();
    private bool _isCompleted;
    private bool _isAborted;
    private bool _isConsumed;
    private bool _disposed;

    public bool IsCompleted => _isCompleted;
    public bool IsAborted => _isAborted;

    public LlmStreamImpl(IAsyncEnumerable<StreamEvent> events)
    {
        _events = events;
    }

    public async IAsyncEnumerator<StreamEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        // 如果流已经被消费过，从缓存中返回
        if (_isConsumed)
        {
            foreach (var cachedEvent in _cachedEvents)
            {
                yield return cachedEvent;
            }
            yield break;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

        await foreach (var streamEvent in _events.ConfigureAwait(false).WithCancellation(linkedCts.Token))
        {
            // 缓存事件
            _cachedEvents.Add(streamEvent);
            yield return streamEvent;

            if (streamEvent is DoneEvent)
            {
                _isCompleted = true;
                _isConsumed = true;
                yield break;
            }
        }

        _isConsumed = true;
    }

    public async Task<LlmResponse> GetResponseAsync(CancellationToken cancellationToken = default)
    {
        // 确保流已经被消费
        if (!_isConsumed)
        {
            // 消费流并缓存事件
            await foreach (var _ in this.WithCancellation(cancellationToken))
            {
                // 事件已经在 GetAsyncEnumerator 中缓存
            }
        }

        // 从缓存的事件中构建响应
        return BuildResponseFromEvents(_cachedEvents);
    }

    private static LlmResponse BuildResponseFromEvents(List<StreamEvent> events)
    {
        // 使用字典按 ContentIndex 累积内容，最后按顺序合并
        var textBlocks = new Dictionary<int, StringBuilder>();
        var thinkingBlocks = new Dictionary<int, StringBuilder>();
        var pendingToolCalls = new Dictionary<int, ToolCallBlock>();
        var finalToolCalls = new List<ToolCallBlock>();

        var usage = default(TokenUsage);
        var model = string.Empty;
        var finishReason = DoneReason.Complete;

        foreach (var streamEvent in events)
        {
            switch (streamEvent)
            {
                case StreamStartEvent start:
                    model = start.Model;
                    break;

                case TextDeltaEvent textDelta:
                    if (!textBlocks.TryGetValue(textDelta.ContentIndex, out var textSb))
                    {
                        textSb = new StringBuilder();
                        textBlocks[textDelta.ContentIndex] = textSb;
                    }
                    textSb.Append(textDelta.Delta);
                    break;

                case ThinkingDeltaEvent thinkingDelta:
                    if (!thinkingBlocks.TryGetValue(thinkingDelta.ContentIndex, out var thinkingSb))
                    {
                        thinkingSb = new StringBuilder();
                        thinkingBlocks[thinkingDelta.ContentIndex] = thinkingSb;
                    }
                    thinkingSb.Append(thinkingDelta.Delta);
                    break;

                case ToolCallStartEvent toolCallStart:
                    // 开始新的工具调用，先完成之前的
                    FinalizePendingToolCalls(finalToolCalls, pendingToolCalls);
                    pendingToolCalls[toolCallStart.ContentIndex] = new ToolCallBlock
                    {
                        Id = toolCallStart.ToolCallId ?? $"call_{toolCallStart.ContentIndex}_{Guid.NewGuid():N}",
                        Name = toolCallStart.ToolName,
                        // 使用空字符串作为初始参数，后续通过 ToolCallDeltaEvent 累加
                        Arguments = JsonSerializer.SerializeToElement("")
                    };
                    break;

                case ToolCallDeltaEvent toolCallDelta:
                    // 累加工具调用参数
                    if (pendingToolCalls.TryGetValue(toolCallDelta.ContentIndex, out var existing))
                    {
                        var currentArgs = existing.Arguments.ValueKind == JsonValueKind.String
                            ? existing.Arguments.GetString() ?? ""
                            : existing.Arguments.GetRawText();
                        pendingToolCalls[toolCallDelta.ContentIndex] = existing with
                        {
                            Arguments = JsonSerializer.SerializeToElement(currentArgs + toolCallDelta.ArgumentsDelta)
                        };
                    }
                    break;

                case ToolCallEndEvent toolCallEnd:
                    finalToolCalls.Add(toolCallEnd.ToolCall);
                    // 从 pending 中移除
                    var removeKey = pendingToolCalls
                        .Where(kvp => kvp.Value.Id == toolCallEnd.ToolCall.Id)
                        .Select(kvp => (int?)kvp.Key)
                        .FirstOrDefault();
                    if (removeKey.HasValue)
                        pendingToolCalls.Remove(removeKey.Value);
                    break;

                case DoneEvent done:
                    finishReason = done.Reason;
                    if (done.Usage != null)
                    {
                        usage = done.Usage;
                    }
                    FinalizePendingToolCalls(finalToolCalls, pendingToolCalls);
                    break;
            }
        }

        // 确保所有待处理的工具调用都被添加
        FinalizePendingToolCalls(finalToolCalls, pendingToolCalls);

        // 按 ContentIndex 顺序合并所有内容块
        var content = new List<ContentBlock>();

        // 收集所有使用过的索引并排序
        var allIndices = new SortedSet<int>();
        foreach (var key in textBlocks.Keys) allIndices.Add(key);
        foreach (var key in thinkingBlocks.Keys) allIndices.Add(key);
        foreach (var key in pendingToolCalls.Keys) allIndices.Add(key);

        // 按索引顺序添加内容
        foreach (var index in allIndices)
        {
            if (thinkingBlocks.TryGetValue(index, out var thinkingSb) && thinkingSb.Length > 0)
            {
                content.Add(new ThinkingBlock { Thinking = thinkingSb.ToString() });
            }
            if (textBlocks.TryGetValue(index, out var textSb) && textSb.Length > 0)
            {
                content.Add(new TextBlock { Text = textSb.ToString() });
            }
        }

        // 添加工具调用（通常在最后）
        content.AddRange(finalToolCalls);

        return new LlmResponse
        {
            Model = model,
            Content = content.ToArray(),
            FinishReason = finishReason,
            Usage = usage
        };
    }

    private static void FinalizePendingToolCalls(List<ToolCallBlock> target, Dictionary<int, ToolCallBlock> pending)
    {
        foreach (var kvp in pending.OrderBy(x => x.Key))
        {
            var toolCall = kvp.Value;

            // 尝试解析 JSON 参数
            var argsStr = toolCall.Arguments.ValueKind == JsonValueKind.String
                ? toolCall.Arguments.GetString() ?? "{}"
                : toolCall.Arguments.GetRawText();

            JsonElement parsedArgs;
            try
            {
                parsedArgs = JsonSerializer.Deserialize<JsonElement>(argsStr);
            }
            catch
            {
                parsedArgs = JsonSerializer.SerializeToElement(new { });
            }

            target.Add(toolCall with { Arguments = parsedArgs });
        }
        pending.Clear();
    }

    public void Abort()
    {
        _isAborted = true;
        _cts.Cancel();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cts.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
