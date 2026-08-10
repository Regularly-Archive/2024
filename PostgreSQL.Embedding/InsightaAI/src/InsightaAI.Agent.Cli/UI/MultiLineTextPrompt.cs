using System.Collections.Generic;
using System.Text;
using Spectre.Console;

namespace InsightaAI.Agent.Cli.UI;

/// <summary>
/// 支持多行输入与行内编辑的文本提示。
///
/// 解决终端粘贴多行内容时被首个换行截断的问题：
/// - Enter 仅在输入队列空转稳定（约 60ms 无新按键，手动按下）时提交；
/// - Enter 且输入队列仍有数据（粘贴注入的换行）时作为换行收集，直到最后一个换行后队列空转稳定才提交；
/// - 粘贴的 \r\n 归一位 \n，避免产生双换行；
/// - Shift+Enter / Ctrl+Enter 手动换行，与发送互不混淆；
/// - ←/→ 逐字符移动光标，Home/End 跳到行首/行尾，支持在任意位置插入与删除；
/// - 超长行按终端宽度自动折行（宽字符不拆开），Ctrl+C 取消输入。
///
/// 光标定位不依赖 Win32 Console API（CursorLeft/CursorTop/SetCursorPosition）：
/// git bash（mintty/winpty）下这些调用会抛 IOException（handle is invalid）或返回
/// 错误坐标。改用 ANSI 光标保存/恢复序列（\u001B[s / \u001B[u）+ 相对移动（\u001B[{n}B /
/// \u001B[{n}C），兼容 mintty、Windows Terminal 及启用 VT 的 ConHost。
/// </summary>
public sealed class MultiLineTextPrompt : IPrompt<string>
{
    private const string PromptMarkup = "[bold green]>[/] ";

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

        // 保存光标位置作为编辑区锚点（提示符之后）。此后 Redraw / PositionCaret 都以
        // 它为原点做相对定位，不再查询终端绝对坐标（winpty 下 Console.CursorLeft 抛错）。
        Console.Write("\u001B[s");
        Console.Out.Flush();

        var buffer = new StringBuilder();
        var caret = 0;    // 光标在 buffer 中的字符索引
        var rowCount = 1; // 编辑区当前占用的物理终端行数（含折行）
        var termWidth = GetTerminalWidth();

        // ReadKey 无法"放回"已读的键，peek 消费的多余键暂存于此，主循环优先取出。
        var pendingKeys = new Queue<ConsoleKeyInfo?>();
        var submitted = false;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rawKey = pendingKeys.Count > 0
                    ? pendingKeys.Dequeue()
                    : await console.Input.ReadKeyAsync(true, cancellationToken).ConfigureAwait(false);
                if (rawKey == null)
                    return string.Empty;

                var key = rawKey.Value;

                // Ctrl+C 取消输入（与 rawKey == null 一样返回空串约定）
                if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                {
                    submitted = true;
                    ClearEditor(rowCount);
                    return string.Empty;
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

                    // 区分手动 Enter 与粘贴注入的换行：短暂等待后输入队列仍空才提交。
                    // 手动按键事件间隔通常远大于粘贴字节流的到达间隔，避免瞬时
                    // KeyAvailable 在快速输入/IME 上屏时误判为"换行"导致 Enter 不发送。
                    if (await IsInputIdleAsync(cancellationToken).ConfigureAwait(false))
                    {
                        submitted = true;
                        return buffer.ToString();
                    }

                    // 粘贴流中的换行：插入 \n，并消费 \r\n 中紧随的 \n（Windows 剪贴板
                    // 默认 \r\n），避免 \r、\n 各触发一次 Enter 产生双换行。
                    Insert("\n");
                    if (Console.KeyAvailable)
                    {
                        var next = await console.Input.ReadKeyAsync(true, cancellationToken).ConfigureAwait(false);
                        if (next is { } nk && (nk.KeyChar == '\r' || nk.KeyChar == '\n'))
                        {
                            // 成对的 \r\n 已归一为单个 \n，后半被消费
                        }
                        else
                        {
                            pendingKeys.Enqueue(next); // 非换行对，放回待主循环处理
                        }
                    }
                    continue;
                }

                switch (key.Key)
                {
                    case ConsoleKey.Backspace:
                        if (caret > 0)
                        {
                            // 代理对（emoji 等）成对删除，避免留下孤立 surrogate
                            var remove = 1;
                            if (caret >= 2 && char.IsLowSurrogate(buffer[caret - 1]) && char.IsHighSurrogate(buffer[caret - 2]))
                                remove = 2;
                            buffer.Remove(caret - remove, remove);
                            caret -= remove;
                            Redraw();
                        }
                        break;

                    case ConsoleKey.Delete:
                        if (caret < buffer.Length)
                        {
                            // 代理对成对删除
                            var remove = 1;
                            if (caret + 1 < buffer.Length && char.IsHighSurrogate(buffer[caret]) && char.IsLowSurrogate(buffer[caret + 1]))
                                remove = 2;
                            buffer.Remove(caret, remove);
                            Redraw();
                        }
                        break;

                    case ConsoleKey.LeftArrow:
                        if (caret > 0)
                        {
                            caret--;
                            PositionCaret();
                        }
                        break;

                    case ConsoleKey.RightArrow:
                        if (caret < buffer.Length)
                        {
                            caret++;
                            PositionCaret();
                        }
                        break;

                    case ConsoleKey.Home:
                        caret = LineStart(caret);
                        PositionCaret();
                        break;

                    case ConsoleKey.End:
                        caret = LineEnd(caret);
                        PositionCaret();
                        break;

                    default:
                        if (char.IsHighSurrogate(key.KeyChar))
                        {
                            // 代理对（emoji 等）：读取低代理合并插入，避免拆成两个
                            // 孤立 surrogate 导致显示/宽度/删除错乱。
                            var hi = key.KeyChar;
                            var next = await console.Input.ReadKeyAsync(true, cancellationToken).ConfigureAwait(false);
                            if (next is { } nk && char.IsLowSurrogate(nk.KeyChar))
                            {
                                Insert(new string(new[] { hi, nk.KeyChar }));
                            }
                            else
                            {
                                Insert(hi.ToString());
                                if (next is not null)
                                    pendingKeys.Enqueue(next);
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
                ClearEditor(rowCount);
        }

        void Insert(string text)
        {
            buffer.Insert(caret, text);
            caret += text.Length;
            Redraw();
        }

        // 等待输入队列空转稳定：约 60ms 内始终无新按键才视为手动 Enter。
        // 粘贴的多行内容会连续到达，期间队列必有数据，因此判为换行。
        async Task<bool> IsInputIdleAsync(CancellationToken ct)
        {
            for (var i = 0; i < 3; i++)
            {
                await Task.Delay(20, ct).ConfigureAwait(false);
                if (Console.KeyAvailable)
                    return false;
            }
            return true;
        }

        // 整块重绘：恢复到锚点，清空编辑区所有物理行并重写整个 buffer（按终端宽度
        // 折行），再把光标定位到 caret。相比增量维护行宽，所有边界统一处理。
        void Redraw()
        {
            Console.Write("\u001B[u"); // 恢复光标到锚点

            var physicalLines = new List<string>();
            foreach (var line in buffer.ToString().Split('\n'))
                physicalLines.AddRange(WrapLine(line));

            var newRowCount = Math.Max(1, physicalLines.Count);
            var total = Math.Max(newRowCount, rowCount);

            for (var i = 0; i < total; i++)
            {
                Console.Write("\u001B[K"); // 清行
                if (i < newRowCount)
                    Console.Write(physicalLines[i]);
                if (i < total - 1)
                    Console.Write("\r\n");
            }

            rowCount = newRowCount;
            PositionCaret();
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
            for (var i = 0; i < caret; i++)
            {
                if (buffer[i] == '\n')
                {
                    row++;
                    col = 0;
                }
                else if (char.IsHighSurrogate(buffer[i]) && i + 1 < buffer.Length && char.IsLowSurrogate(buffer[i + 1]))
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
                    var w = GetWidth(buffer[i]);
                    if (col + w > termWidth)
                    {
                        row++;
                        col = 0;
                    }
                    col += w;
                }
            }

            Console.Write("\u001B[u"); // 恢复光标到锚点
            if (row > 0)
                Console.Write($"\u001B[{row}B"); // 下移 row 行
            if (col > 0)
                Console.Write($"\u001B[{col}C"); // 右移 col 列
            Console.Out.Flush();
        }

        // 清理编辑区：还原锚点并清空当前占用的所有物理行（取消/异常时调用）。
        void ClearEditor(int lines)
        {
            Console.Write("\u001B[u");
            for (var i = 0; i < lines; i++)
            {
                Console.Write("\u001B[K");
                if (i < lines - 1)
                    Console.Write("\r\n");
            }
            Console.Out.Flush();
        }

        int LineStart(int index)
        {
            while (index > 0 && buffer[index - 1] != '\n')
                index--;
            return index;
        }

        int LineEnd(int index)
        {
            while (index < buffer.Length && buffer[index] != '\n')
                index++;
            return index;
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
