using InsightaAI.Agent.Cli.Extensions;
using InsightaAI.Agent.Cli.Localization;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Models;
using Spectre.Console;
using System.Diagnostics;

namespace InsightaAI.Agent.Cli.UI;

/// <summary>
/// Agent 事件渲染器 - 处理流式事件的终端显示
/// </summary>
public class EventRenderer : IDisposable
{
    private readonly HangingIndentWriter _assistantWriter = new(
        AnsiConsole.Console,
        "[dim]● [/]",
        "  ");
    private readonly IAnsiConsole _console;
    private bool _isThinking;
    private CancellationTokenSource? _thinkingCts;
    private Task _thinkingTask = Task.CompletedTask;
    private readonly Dictionary<string, string> _pendingTools = [];
    private readonly ToolProgressWindow _toolProgressWindow = new();
    private LiveDisplayContext? _toolProgressLiveContext;
    private TaskCompletionSource? _toolProgressLiveCompletion;
    private Task _toolProgressLiveTask = Task.CompletedTask;
    private bool _hasReceivedToolProgress;

    public EventRenderer(IAnsiConsole? console = null)
    {
        _console = console ?? AnsiConsole.Console;
    }

    /// <summary>
    /// 累积的完整文本
    /// </summary>
    public string FullText { get; private set; } = "";

    /// <summary>
    /// 处理 Agent 事件
    /// </summary>
    public async Task HandleEventAsync(AgentEvent agentEvent)
    {
        switch (agentEvent)
        {
            case AgentTurnStartEvent:
                break;

            case AgentLlmStreamEvent llmEvent:
                await HandleStreamEventAsync(llmEvent.StreamEvent);
                break;

            case AgentErrorEvent errorEvent:
                await StopThinkingAsync();
                CloseAssistantSegment();
                WriteIndentedMarkup(errorEvent.ErrorMessage, "◆ ", "  ", "red");
                break;

            case AgentToolStartEvent toolStart:
                await HandleToolStartAsync(toolStart);
                break;

            case AgentToolProgressEvent toolProgress:
                await HandleToolProgressAsync(toolProgress);
                break;

            case AgentToolEndEvent toolEnd:
                await HandleToolEndAsync(toolEnd);
                break;

            case AgentTurnEndEvent completeEvent:
                await HandleCompleteAsync(completeEvent);
                break;

            case AgentRoundEndEvent:
                // LLM 流已结束，立即停止 thinking spinner
                // 否则从流结束到工具开始执行之间 spinner 仍在显示
                await StopThinkingAsync();
                break;

            case AgentContextCompactedEvent compactedEvent:
                HandleContextCompacted(compactedEvent);
                break;
        }
    }

    private async Task HandleStreamEventAsync(StreamEvent streamEvent)
    {
        switch (streamEvent)
        {
            case ThinkingStartEvent:
                StartThinking();
                break;

            case ThinkingDeltaEvent thinkingDelta when _isThinking:
                // 累积 thinking 文本（当前不显示）
                break;

            case ThinkingEndEvent:
                await StopThinkingAsync();
                break;

            case TextStartEvent:
                await StopThinkingAsync();
                // A response may contain multiple text segments separated by tool calls.
                // Delay the marker until the first non-empty delta so empty segments stay invisible.
                CloseAssistantSegment();
                break;

            case TextDeltaEvent textDelta:
                await StopThinkingAsync();
                if (!string.IsNullOrEmpty(textDelta.Delta))
                {
                    FullText += textDelta.Delta;
                    _assistantWriter.Write(textDelta.Delta);
                }
                break;
            case TextEndEvent:
                await StopThinkingAsync();
                CloseAssistantSegment();
                break;
        }
    }

    private async Task HandleToolStartAsync(AgentToolStartEvent toolStart)
    {
        await StopThinkingAsync();
        CloseAssistantSegment();

        var toolArgs = toolStart.Arguments.Truncate(50);
        var displayText = $"{toolStart.ToolName}({EscapeMarkup(toolArgs)})";
        _pendingTools[toolStart.ToolCallId] = displayText;
        _toolProgressWindow.Begin(toolStart.ToolCallId, displayText);
    }

    private async Task HandleToolProgressAsync(AgentToolProgressEvent toolProgress)
    {
        await StopThinkingAsync();
        CloseAssistantSegment();
        _hasReceivedToolProgress = true;
        _toolProgressWindow.Apply(toolProgress.ToolCallId, toolProgress.Progress);
        await EnsureToolProgressLiveAsync();
    }

    private async Task HandleToolEndAsync(AgentToolEndEvent toolEnd)
    {
        _toolProgressWindow.Complete(toolEnd.ToolCallId);
        await StopToolProgressLiveAsync();

        var toolDisplay = _pendingTools.TryGetValue(toolEnd.ToolCallId, out var displayText)
            ? displayText
            : EscapeMarkup(toolEnd.ToolName);

        // A parallel tool result can arrive after other ToolStart events. Render the call and
        // result together only when its matching ToolEnd arrives, so the visual hierarchy remains
        // unambiguous without relying on terminal cursor rewrites.
        CloseAssistantSegment();
        _console.MarkupLine($"[dim]○ {toolDisplay}[/]");

        if (toolEnd.IsError)
        {
            var errorMessage = (toolEnd.ResultPreview ?? "error").Truncate(60);
            WriteIndentedMarkup(errorMessage, "  ⎿ ", "    ", "red");
        }
        else
        {
            WriteIndentedMarkup(toolEnd.ResultPreview ?? "", "  ⎿ ", "    ", "green");
        }

        _console.WriteLine();
        _pendingTools.Remove(toolEnd.ToolCallId);

        if (_hasReceivedToolProgress && _toolProgressWindow.HasActiveEntries)
            await EnsureToolProgressLiveAsync();
    }

    private async Task HandleCompleteAsync(AgentTurnEndEvent completeEvent)
    {
        await StopThinkingAsync();

        CloseAssistantSegment();

        ShowTokenUsage(completeEvent.Result!.Usage, completeEvent.Result!.EstimatedContextTokens,
            completeEvent.Result!.AvailableInputTokens);
    }

    private void HandleContextCompacted(AgentContextCompactedEvent compactedEvent)
    {
        CloseAssistantSegment();
        _console.WriteLine();
        _console.MarkupLine(
            CliStrings.Format("ChatAutoCompactedFormat", compactedEvent.Strategy, compactedEvent.PreCompactMessages, compactedEvent.PostCompactMessages, compactedEvent.PreCompactTokens, compactedEvent.PostCompactTokens));
        _console.WriteLine();
    }

    private void ShowTokenUsage(TokenUsage? usage, int estimatedContextTokens = 0, int availableInputTokens = 0)
    {
        if (usage == null) return;

        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap());

        var cacheText = usage.CacheHitTokens > 0
            ? $"[yellow]{usage.CacheHitTokens:N0}⚡[/]"
            : "[dim]0⚡[/]";

        // 上下文百分比
        var contextText = "[dim]🪟 -[/]";
        if (availableInputTokens > 0 && estimatedContextTokens > 0)
        {
            var contextPercent = (double)estimatedContextTokens / availableInputTokens * 100;
            var contextColor = contextPercent switch
            {
                >= 90 => "red",
                >= 70 => "yellow",
                _ => "green"
            };
            contextText = $"[{contextColor}]🪟 {contextPercent:F0}%[/]";
        }

        grid.AddRow(
            CliStrings.ChatTokenUsageLabel,
            $"[green]{usage.InputTokens:N0} ↑[/]",
            $"[blue]{usage.OutputTokens:N0} ↓[/]",
            cacheText,
            contextText);

        _console.WriteLine();
        _console.Write(grid);
    }

    /// <summary>
    /// 显示用户中断提示
    /// </summary>
    public async Task ShowInterruptedAsync()
    {
        // 停止 thinking spinner 并等待后台任务完全退出
        // 否则 AnsiConsole.Status 的渲染循环可能仍在运行，干扰后续输出
        await StopThinkingAsync();

        CloseAssistantSegment();

        _console.MarkupLine(CliStrings.ChatInterruptedTitle);
        _console.MarkupLine(CliStrings.ChatInterruptedHint);
    }

    /// <summary>
    /// 重置状态（用于新一轮对话）
    /// </summary>
    public void Reset()
    {
        FullText = "";
        _assistantWriter.Reset();
        _pendingTools.Clear();
    }

    private async Task EnsureToolProgressLiveAsync()
    {
        // StartAsync invokes its callback asynchronously. _toolProgressLiveContext is assigned in
        // that callback, so it cannot be used as the startup guard: several rapid progress events
        // could otherwise create multiple Live regions before the first callback runs.
        if (_toolProgressLiveCompletion is not null ||
            !_console.Profile.Capabilities.Interactive)
            return;

        _toolProgressLiveCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _toolProgressLiveTask = _console.Live(_toolProgressWindow.Render())
            .AutoClear(true)
            .StartAsync(async context =>
            {
                _toolProgressLiveContext = context;
                while (!_toolProgressLiveCompletion.Task.IsCompleted)
                {
                    context.UpdateTarget(_toolProgressWindow.Render());
                    await Task.WhenAny(
                        _toolProgressLiveCompletion.Task,
                        Task.Delay(TimeSpan.FromMilliseconds(200)));
                }
            });

        await Task.Yield();
        if (_toolProgressLiveTask.IsFaulted)
        {
            try
            {
                await _toolProgressLiveTask;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Tool progress live rendering is unavailable: {exception.Message}");
                _toolProgressLiveCompletion = null;
                _toolProgressLiveContext = null;
                _toolProgressLiveTask = Task.CompletedTask;
            }
        }
    }

    private async Task StopToolProgressLiveAsync()
    {
        if (_toolProgressLiveCompletion is null)
            return;

        try
        {
            _toolProgressLiveCompletion.TrySetResult();
            await _toolProgressLiveTask;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Tool progress live rendering stopped unexpectedly: {exception.Message}");
        }
        finally
        {
            _toolProgressLiveCompletion = null;
            _toolProgressLiveContext = null;
            _toolProgressLiveTask = Task.CompletedTask;
        }
    }

    private void CloseAssistantSegment()
    {
        if (_assistantWriter.HasStarted)
        {
            _assistantWriter.EnsureLineBreak();
            _assistantWriter.Reset();
        }
    }

    private void WriteIndentedMarkup(
        string text,
        string firstPrefix,
        string continuationIndent,
        string color)
    {
        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var prefix = i == 0 ? firstPrefix : continuationIndent;
            _console.MarkupLine($"[{color}]{prefix}{EscapeMarkup(lines[i])}[/]");
        }
    }

    private void StartThinking()
    {
        _isThinking = true;
        _thinkingCts = new CancellationTokenSource();
        var ct = _thinkingCts.Token;
        _thinkingTask = Task.Run(async () =>
        {
            try
            {
                var dotCount = 0;

                await _console.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync(CliStrings.ChatThinkingInitial, async ctx =>
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            await Task.Delay(400, ct);
                            dotCount = (dotCount % 3) + 1;
                            ctx.Status = CliStrings.Format("ChatThinkingProgressFormat", new string('.', dotCount));
                        }
                    });
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    private async Task StopThinkingAsync()
    {
        if (!_isThinking || _thinkingCts == null) return;

        try
        {
            _thinkingCts.Cancel();
            await _thinkingTask;

            // 等待 Status 组件的渲染循环完全退出
            // Spectre.Console 的 Status 使用定时渲染循环（~80ms 间隔），
            // 回调退出后渲染循环可能仍有 1 帧残留，需要等待足够时间
            await Task.Delay(150);
        }
        catch (OperationCanceledException)
        {
            // 预期的取消操作
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error stopping thinking status: {ex.Message}");
        }
        finally
        {
            _thinkingCts.Dispose();
            _thinkingCts = null;
            _isThinking = false;
        }
    }

    private static string EscapeMarkup(string text)
    {
        return text.Replace("[", "[[").Replace("]", "]]");
    }

    public void Dispose()
    {
        _toolProgressLiveCompletion?.TrySetResult();
        _thinkingCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
