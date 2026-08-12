using System.Collections.Generic;
using System.Text;
using Spectre.Console;

namespace InsightaAI.Agent.Cli.UI;

/// <summary>
/// 支持多行输入与行内编辑的文本提示。
///
/// 输入策略：
/// - 仅当输入源提供完整 bracketed-paste 协议边界时，粘贴内容才折叠为原子块；
/// - 无协议边界的文本一律作为普通输入处理，绝不按到达速度或字符数猜测粘贴；
/// - Enter 在输入队列空转稳定（约 60ms）后提交；连续到达的换行仍可作为多行内容收集；
/// - 已确认的粘贴会将 \r\n 归一为 \n，避免产生双换行；
/// - Shift+Enter / Ctrl+Enter 手动换行，与发送互不混淆；
/// - ←/→ 逐字符移动光标，Home/End 跳到行首/行尾，支持在任意位置插入与删除；
/// - 超长行按终端宽度自动折行（宽字符不拆开），Ctrl+C 取消输入。
///
/// 光标定位不依赖 Win32 Console API（CursorLeft/CursorTop/SetCursorPosition）：
/// git bash（mintty/winpty）下这些调用会抛 IOException（handle is invalid）或返回
/// 错误坐标。改用相对光标移动序列（\u001B[{n}A / \u001B[{n}B / \u001B[{n}C），
/// 通过维护编辑区内的相对行号回到锚点，兼容 mintty、Windows Terminal 及启用 VT 的 ConHost。
/// </summary>
public sealed class MultiLineTextPrompt : IPrompt<string>
{
    private const string PromptMarkup = "[bold green]>[/] ";
    private const string PromptIndent = "  ";
    private const string EnableBracketedPaste = "\u001B[?2004h";
    private const string DisableBracketedPaste = "\u001B[?2004l";
    private readonly IReadOnlyList<SlashCommand> _slashCommands;

    public MultiLineTextPrompt(IReadOnlyList<SlashCommand>? slashCommands = null)
    {
        _slashCommands = slashCommands ?? [];
    }

    /// <inheritdoc />
    public string Show(IAnsiConsole console)
    {
        return ShowAsync(console, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async Task<string> ShowAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(console);

        console.Markup(PromptMarkup);
        Console.Out.Flush();

        var bracketedPasteEnabled = Terminal.SupportsBracketedPaste;
        if (bracketedPasteEnabled)
        {
            Console.Write(EnableBracketedPaste);
            Console.Out.Flush();
        }

        var buffer = new PromptInputBuffer();
        var inputRowCount = 1; // 输入文本当前占用的物理终端行数（含折行）
        var renderedRowCount = 1; // 输入文本与命令候选区合计占用的物理终端行数
        var cursorRow = 0; // 当前光标相对编辑区锚点的物理行号
        // 首行的提示符与后续行的缩进均占两列，内容折行需要排除这部分宽度。
        var termWidth = Math.Max(1, GetTerminalWidth() - PromptIndent.Length);

        using var input = PromptInputSourceFactory.Create(console.Input, bracketedPasteEnabled);
        var submitted = false;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var inputEvent = await input.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (inputEvent is PromptEndOfInputEvent)
                    return string.Empty;

                if (inputEvent is PromptPasteInputEvent paste)
                {
                    buffer.InsertPaste(NormalizeLineEndings(paste.Text));
                    Redraw();
                    continue;
                }

                var key = ((PromptKeyInputEvent)inputEvent).Key;

                // Ctrl+C 取消输入（与 rawKey == null 一样返回空串约定）
                if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                {
                    submitted = true;
                    ClearEditor(renderedRowCount);
                    return null!;
                }

                if (key.Key == ConsoleKey.Enter)
                {
                    // 部分终端（如 Windows ConHost）不会把 Shift+Enter 的 Shift 修饰传给
                    // ReadKey，因此手动换行同时接受 Shift 或 Ctrl 修饰，扩大兼容面。
                    var hasModifier = key.Modifiers.HasFlag(ConsoleModifiers.Shift)
                                   || key.Modifiers.HasFlag(ConsoleModifiers.Control);

                    if (hasModifier)
                    {
                        Insert("\n");
                        continue;
                    }

                    // 短暂等待后输入队列仍空才提交。无协议边界的连续换行会继续
                    // 作为多行文本收集，但不会触发粘贴折叠。
                    if (await IsInputIdleAsync(cancellationToken).ConfigureAwait(false))
                    {
                        ClearSlashCommandSuggestions();
                        RevealPastedContent();
                        submitted = true;
                        return buffer.Text;
                    }

                    // 连续输入中的换行：消费 \r\n 中紧随的 \n，避免双换行。
                    // 若这一对之后已经空闲，它是一次手动 Enter 的双事件表示，应提交而非换行。
                    if (input.IsInputAvailable)
                    {
                        var nextInput = await input.ReadAsync(cancellationToken).ConfigureAwait(false);
                        if (nextInput is PromptKeyInputEvent { Key: var nk } && (nk.KeyChar == '\r' || nk.KeyChar == '\n'))
                        {
                            if (await IsInputIdleAsync(cancellationToken).ConfigureAwait(false))
                            {
                                ClearSlashCommandSuggestions();
                                RevealPastedContent();
                                submitted = true;
                                return buffer.Text;
                            }

                            Insert("\n");
                            // 成对的 \r\n 已归一为单个 \n，后半被消费
                        }
                        else
                        {
                            Insert("\n");
                            input.PushBack(nextInput); // 非换行事件交还给主循环处理
                        }
                    }
                    else
                    {
                        Insert("\n");
                    }
                    continue;
                }

                switch (key.Key)
                {
                    case ConsoleKey.Tab:
                        if (TryCompleteSlashCommand())
                        {
                            Redraw();
                        }
                        else
                        {
                            Insert("\t");
                        }
                        break;

                    case ConsoleKey.Backspace:
                        if (buffer.Backspace())
                        {
                            Redraw();
                        }
                        break;

                    case ConsoleKey.Delete:
                        if (buffer.Delete())
                        {
                            Redraw();
                        }
                        break;

                    case ConsoleKey.LeftArrow:
                        if (buffer.MoveLeft())
                        {
                            PositionCaret();
                        }
                        break;

                    case ConsoleKey.RightArrow:
                        if (buffer.MoveRight())
                        {
                            PositionCaret();
                        }
                        break;

                    case ConsoleKey.Home:
                        buffer.MoveHome();
                        PositionCaret();
                        break;

                    case ConsoleKey.End:
                        buffer.MoveEnd();
                        PositionCaret();
                        break;

                    default:
                        if (char.IsHighSurrogate(key.KeyChar))
                        {
                            // 代理对（emoji 等）：读取低代理合并插入，避免拆成两个
                            // 孤立 surrogate 导致显示/宽度/删除错乱。
                            var hi = key.KeyChar;
                            var nextInput = await input.ReadAsync(cancellationToken).ConfigureAwait(false);
                            if (nextInput is PromptKeyInputEvent { Key: var nk } && char.IsLowSurrogate(nk.KeyChar))
                            {
                                Insert(new string(new[] { hi, nk.KeyChar }));
                            }
                            else
                            {
                                Insert(hi.ToString());
                                input.PushBack(nextInput);
                            }
                            break;
                        }

                        // \t 保留（粘贴带缩进代码需要）；\r 走 Enter 路径已归一为 \n。
                        if (key.KeyChar == '\t' || !char.IsControl(key.KeyChar))
                            Insert(key.KeyChar.ToString());
                        break;
                }
            }
        }
        finally
        {
            // 未正常提交（异常/取消/ReadKey 返回 null）时清理编辑区，避免残留
            if (!submitted)
                ClearEditor(renderedRowCount);

            if (bracketedPasteEnabled)
            {
                Console.Write(DisableBracketedPaste);
                Console.Out.Flush();
            }
        }

        void Insert(string text)
        {
            buffer.InsertText(text);
            Redraw();
        }

        // 等待输入队列空转稳定：约 60ms 内始终无新按键才视为手动 Enter。
        // 粘贴的多行内容会连续到达，期间队列必有数据，因此判为换行。
        async Task<bool> IsInputIdleAsync(CancellationToken ct)
        {
            for (var i = 0; i < 3; i++)
            {
                await Task.Delay(20, ct).ConfigureAwait(false);
                if (input.IsInputAvailable)
                    return false;
            }
            return true;
        }

        static string NormalizeLineEndings(string text) =>
            text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        // 占位符仅用于编辑阶段。提交前将其替换为原文，确保用户回看终端历史、
        // Agent 接收的输入与会话持久化内容一致。
        void RevealPastedContent()
        {
            if (!buffer.ContainsPaste)
                return;

            MoveToAnchor();
            for (var i = 0; i < inputRowCount; i++)
            {
                Console.Write("\u001B[K");
                if (i < inputRowCount - 1)
                    Console.Write("\r\n");
            }

            MoveFromLastRenderedRowToAnchor(inputRowCount);
            Console.Write(buffer.Text);
            Console.Out.Flush();
        }

        void ClearSlashCommandSuggestions()
        {
            var suggestionRows = renderedRowCount - inputRowCount;
            if (suggestionRows <= 0)
                return;

            MoveToAnchor();
            Console.Write("\r");
            Console.Write($"\u001B[{inputRowCount}B");
            for (var i = 0; i < suggestionRows; i++)
            {
                Console.Write("\u001B[K");
                if (i < suggestionRows - 1)
                    Console.Write("\r\n");
            }

            renderedRowCount = inputRowCount;
            PositionCaret();
        }

        // 整块重绘：恢复到锚点，清空编辑区所有物理行并重写整个 buffer（按终端宽度
        // 折行），再把光标定位到 caret。相比增量维护行宽，所有边界统一处理。
        void Redraw()
        {
            MoveToAnchor();

            var physicalLines = new List<string>();
            foreach (var line in buffer.DisplayText.Split('\n'))
                physicalLines.AddRange(WrapLine(line));

            var newRowCount = Math.Max(1, physicalLines.Count);
            var suggestions = GetSlashCommandSuggestions();
            var commandColumnWidth = suggestions.Count == 0
                ? 0
                : suggestions.Max(static suggestion => suggestion.Name.Length);
            var newRenderedRowCount = newRowCount + (suggestions.Count > 0 ? suggestions.Count + 1 : 0);
            var total = Math.Max(newRenderedRowCount, renderedRowCount);

            for (var i = 0; i < total; i++)
            {
                Console.Write("\u001B[K"); // 清行
                if (i < newRowCount)
                {
                    if (i > 0)
                        Console.Write(PromptIndent);
                    Console.Write(physicalLines[i]);
                }
                else if (i > newRowCount && i < newRenderedRowCount)
                {
                    Console.Write(PromptIndent);
                    var suggestion = suggestions[i - newRowCount - 1];
                    Console.Write($"{suggestion.Name.PadRight(commandColumnWidth)}  {suggestion.Description}");
                }
                if (i < total - 1)
                    Console.Write("\r\n");
            }

            inputRowCount = newRowCount;
            renderedRowCount = newRenderedRowCount;
            MoveFromLastRenderedRowToAnchor(total);
            cursorRow = 0;
            PositionCaret();
        }

        IReadOnlyList<SlashCommand> GetSlashCommandSuggestions()
        {
            var text = buffer.Text;
            if (buffer.ContainsPaste || text.Contains('\n') || !text.StartsWith('/') || _slashCommands.Count == 0)
                return [];

            return _slashCommands
                .Where(command => !command.Name.Equals(text, StringComparison.OrdinalIgnoreCase)
                    && command.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        bool TryCompleteSlashCommand()
        {
            if (!buffer.IsCaretAtEnd)
                return false;

            var suggestions = GetSlashCommandSuggestions();
            if (suggestions.Count != 1)
                return false;

            var command = suggestions[0];
            var input = buffer.Text;
            buffer.InsertText(command.Name[input.Length..]);
            if (command.AcceptsArgument)
                buffer.InsertText(" ");
            return true;
        }

        // 按终端宽度把一行切成多段物理行；宽字符（2 列）在边界不拆开，剩余不足时换行。
        List<string> WrapLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            var width = 0;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                // 代理对作为整体计算宽度，避免折行时被拆开
                if (char.IsHighSurrogate(c) && i + 1 < line.Length && char.IsLowSurrogate(line[i + 1]))
                {
                    if (width + 2 > termWidth)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                        width = 0;
                    }
                    current.Append(c).Append(line[i + 1]);
                    width += 2;
                    i++;
                    continue;
                }

                var w = GetWidth(c);
                if (width + w > termWidth)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    width = 0;
                }
                current.Append(c);
                width += w;
            }

            result.Add(current.ToString());
            return result;
        }

        // 恢复到锚点后按（row, col）相对移动定位光标；row/col 均按物理行/列计（含折行），
        // 折行规则与 WrapLine 一致（宽字符不拆、放不下则换行）。
        void PositionCaret()
        {
            var row = 0;
            var col = 0;
            var displayBeforeCaret = buffer.DisplayTextBeforeCaret;
            for (var i = 0; i < displayBeforeCaret.Length; i++)
            {
                if (displayBeforeCaret[i] == '\n')
                {
                    row++;
                    col = 0;
                }
                else if (char.IsHighSurrogate(displayBeforeCaret[i]) && i + 1 < displayBeforeCaret.Length && char.IsLowSurrogate(displayBeforeCaret[i + 1]))
                {
                    // 代理对整体占 2 列，避免折行拆开
                    if (col + 2 > termWidth)
                    {
                        row++;
                        col = 0;
                    }
                    col += 2;
                    i++;
                }
                else
                {
                    var w = GetWidth(displayBeforeCaret[i]);
                    if (col + w > termWidth)
                    {
                        row++;
                        col = 0;
                    }
                    col += w;
                }
            }

            MoveToAnchor();
            if (row > 0)
            {
                // 首行从提示符后的锚点开始；显式换行和自动折行后的物理行从第 0 列
                // 开始。先回车再下移，避免把提示符宽度错误叠加到后续行。
                Console.Write("\r");
                Console.Write($"\u001B[{row}B"); // 下移 row 行
                Console.Write($"\u001B[{PromptIndent.Length}C"); // 对齐后续行的提示符缩进
            }
            if (col > 0)
                Console.Write($"\u001B[{col}C"); // 右移 col 列
            cursorRow = row;
            Console.Out.Flush();
        }

        // 清理编辑区：还原锚点并清空当前占用的所有物理行（取消/异常时调用）。
        void ClearEditor(int lines)
        {
            MoveToAnchor();
            for (var i = 0; i < lines; i++)
            {
                Console.Write("\u001B[K");
                if (i < lines - 1)
                    Console.Write("\r\n");
            }
            Console.Out.Flush();
        }

        // 当前光标始终由 PositionCaret 维护在编辑区内。回车保证第 0 列，随后按已知
        // 行号上移，再跳过首行提示符的宽度，即可回到提示符后的编辑区锚点。
        void MoveToAnchor()
        {
            Console.Write("\r");
            if (cursorRow > 0)
                Console.Write($"\u001B[{cursorRow}A");
            Console.Write($"\u001B[{PromptIndent.Length}C");
        }

        // 重绘完成时光标位于最后一个已处理物理行。回到首行并跳过提示符，供下一次
        // 定位或提交前揭示原文使用。
        void MoveFromLastRenderedRowToAnchor(int renderedRows)
        {
            Console.Write("\r");
            if (renderedRows > 1)
                Console.Write($"\u001B[{renderedRows - 1}A");
            Console.Write($"\u001B[{PromptIndent.Length}C");
        }

    }

    /// <summary>
    /// 获取终端宽度（列数）。winpty/mintty 下 Win32 控制台 API 不可用抛 IOException，
    /// 兜底 80；Windows Terminal / ConHost / Linux 下可正常读取。
    /// </summary>
    private static int GetTerminalWidth()
    {
        try
        {
            return Math.Max(20, Console.WindowWidth);
        }
        catch (IOException)
        {
            return 80;
        }
    }

    /// <summary>
    /// 估算字符的终端显示宽度：全角 CJK 字符为 2 列，TAB 近似为 8 列（终端默认
    /// tab stop），其余为 1 列。与 Spectre.Console 内部 UnicodeCalculator.GetWidth
    /// 的简化等价实现（不含 emoji 区 0x1F300+ 与组合字符的完整 wcwidth 表）。
    /// </summary>
    private static int GetWidth(char c)
    {
        if (c == '\t')
            return 8;

        if (c >= 0x1100 && (c <= 0x115F || c == 0x2329 || c == 0x232A
            || (c >= 0x2E80 && c <= 0xA4CF && c != 0x303F)
            || (c >= 0xAC00 && c <= 0xD7A3)
            || (c >= 0xF900 && c <= 0xFAFF)
            || (c >= 0xFE30 && c <= 0xFE4F)
            || (c >= 0xFF00 && c <= 0xFF60)
            || (c >= 0xFFE0 && c <= 0xFFE6)))
        {
            return 2;
        }

        return 1;
    }
}
