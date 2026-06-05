using System.Diagnostics;
using InsightaAI.Agent.Cli.Extensions;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Models;
using Spectre.Console;

namespace InsightaAI.Agent.Cli.UI;

/// <summary>
/// Agent 事件渲染器 - 处理流式事件的终端显示
/// </summary>
public class EventRenderer : IDisposable
{
    private bool _headerShown;
    private bool _isThinking;
    private CancellationTokenSource? _thinkingCts;
    private Task _thinkingTask = Task.CompletedTask;
    private readonly Dictionary<string, string> _pendingTools = [];

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
            case AgentStartEvent:
                break;

            case AgentLlmStreamEvent llmEvent:
                await HandleStreamEventAsync(llmEvent.StreamEvent);
                break;

            case AgentToolStartEvent toolStart:
                await HandleToolStartAsync(toolStart);
                break;

            case AgentToolEndEvent toolEnd:
                HandleToolEnd(toolEnd);
                break;

            case AgentCompleteEvent completeEvent:
                await HandleCompleteAsync(completeEvent);
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

            case TextStartEvent:
                await StopThinkingAsync();
                // 不在这里显示 header，等实际有文本时再显示
                break;

            case TextDeltaEvent textDelta:
                await StopThinkingAsync();
                if (!string.IsNullOrEmpty(textDelta.Delta))
                {
                    EnsureHeader();
                    FullText += textDelta.Delta;
                    AnsiConsole.Write("{0}", textDelta.Delta);
                }
                break;
            case TextEndEvent textEnd:
                await StopThinkingAsync();
                AnsiConsole.WriteLine();
                break;
        }
    }

    private async Task HandleToolStartAsync(AgentToolStartEvent toolStart)
    {
        await StopThinkingAsync();
        EnsureHeader();

        var toolArgs = toolStart.Arguments.Truncate(50);
        var displayText = $"{toolStart.ToolName}({EscapeMarkup(toolArgs)})";
        _pendingTools[toolStart.ToolCallId] = displayText;
        AnsiConsole.WriteLine();
    }

    private void HandleToolEnd(AgentToolEndEvent toolEnd)
    {
        var toolDisplay = _pendingTools.TryGetValue(toolEnd.ToolCallId, out var text)
            ? text
            : toolEnd.ToolName;

        AnsiConsole.WriteLine();

        if (toolEnd.IsError)
        {
            var errorMessage = (toolEnd.ResultPreview ?? "error").Truncate(60);

            AnsiConsole.MarkupLine($"[red]●[/] [dim]{toolDisplay}[/]");
            AnsiConsole.MarkupLine($"[red]⎿ {EscapeMarkup(errorMessage)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]●[/] [dim]{toolDisplay}[/]");
            AnsiConsole.MarkupLine($"[green]⎿ {EscapeMarkup(toolEnd.ResultPreview ?? "")}[/]");
        }

        AnsiConsole.WriteLine();
        _pendingTools.Remove(toolEnd.ToolCallId);
    }

    private async Task HandleCompleteAsync(AgentCompleteEvent completeEvent)
    {
        await StopThinkingAsync();

        // 只有当有文本输出时才添加空行
        if (_headerShown && !string.IsNullOrEmpty(FullText))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine();
        }

        ShowTokenUsage(completeEvent.Result.Usage);
    }

    private void ShowTokenUsage(TokenUsage? usage)
    {
        if (usage == null) return;

        var total = usage.InputTokens + usage.OutputTokens;
        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap());

        var cacheText = usage.CacheHitTokens > 0
            ? $"[yellow]{usage.CacheHitTokens}⚡[/]"
            : "[dim]0⚡[/]";

        grid.AddRow(
            "[grey]Tokens:[/]",
            $"[green]{usage.InputTokens}↑[/]",
            $"[blue]{usage.OutputTokens}↓[/]",
            cacheText,
            $"[dim]{total}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.Write(grid);
    }

    /// <summary>
    /// 重置状态（用于新一轮对话）
    /// </summary>
    public void Reset()
    {
        FullText = "";
        _headerShown = false;
        _pendingTools.Clear();
    }

    private void EnsureHeader()
    {
        if (!_headerShown)
        {
            _headerShown = true;
            AnsiConsole.WriteLine();
            AnsiConsole.Markup("[dim]● [/]");
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
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Thinking...", async _ =>
                    {
                        await Task.Delay(Timeout.Infinite, ct);
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
        _thinkingCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
