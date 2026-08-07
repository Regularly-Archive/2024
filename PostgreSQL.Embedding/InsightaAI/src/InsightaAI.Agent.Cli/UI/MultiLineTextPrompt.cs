using System.Text;
using Spectre.Console;

namespace InsightaAI.Agent.Cli.UI;

/// <summary>
/// 支持多行输入与行内编辑的文本提示。
///
/// 解决终端粘贴多行内容时被首个换行截断的问题：
/// - Enter 仅当输入队列为空（手动按下）时提交；
/// - Enter 且输入队列非空（粘贴注入的换行）时作为换行收集，直到最后一个换行后队列为空才提交；
/// - Shift+Enter / Ctrl+Enter 手动换行，与发送互不混淆；
/// - ←/→ 逐字符移动光标，Home/End 跳到行首/行尾，支持在任意位置插入与删除。
///
/// 光标定位不依赖 Win32 Console API（CursorLeft/CursorTop/SetCursorPosition）：
/// git bash（mintty/winpty）下这些调用会抛 IOException（handle is invalid）或返回
/// 错误坐标。改用 DEC 光标保存/恢复序列（\x1b7 / \x1b8）+ 相对移动（\x1b[{n}B /
/// \x1b[{n}C），兼容 mintty、Windows Terminal 及启用 VT 的 ConHost。
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
        Console.Write("\x1b7");
        Console.Out.Flush();

        var buffer = new StringBuilder();
        var caret = 0;   // 光标在 buffer 中的字符索引
        var rowCount = 1; // 编辑区当前占用的终端行数

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rawKey = await console.Input.ReadKeyAsync(true, cancellationToken).ConfigureAwait(false);
            if (rawKey == null)
                return string.Empty;

            var key = rawKey.Value;

            if (key.Key == ConsoleKey.Enter)
            {
                // 部分终端（如 Windows ConHost）不会把 Shift+Enter 的 Shift 修饰传给
                // ReadKey，因此手动换行同时接受 Shift 或 Ctrl 修饰，扩大兼容面。
                var hasModifier = key.Modifiers.HasFlag(ConsoleModifiers.Shift)
                               || key.Modifiers.HasFlag(ConsoleModifiers.Control);

                // 手动 Enter（无修饰且输入队列为空）→ 提交；否则按换行收集
                if (!hasModifier && !Console.KeyAvailable)
                    return buffer.ToString();

                Insert('\n');
                continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Backspace:
                    if (caret > 0)
                    {
                        buffer.Remove(caret - 1, 1);
                        caret--;
                        Redraw();
                    }
                    break;

                case ConsoleKey.Delete:
                    if (caret < buffer.Length)
                    {
                        buffer.Remove(caret, 1);
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
                    if (!char.IsControl(key.KeyChar))
                        Insert(key.KeyChar);
                    break;
            }
        }

        void Insert(char c)
        {
            buffer.Insert(caret, c);
            caret++;
            Redraw();
        }

        // 整块重绘：恢复到锚点，清空编辑区所有行并重写整个 buffer，再把光标定位到 caret。
        // 相比增量维护行宽，所有边界（全角、跨行合并、中间编辑）统一处理。
        void Redraw()
        {
            Console.Write("\x1b8"); // 恢复光标到锚点

            var lines = buffer.ToString().Split('\n');
            var newRowCount = Math.Max(1, lines.Length);
            var total = Math.Max(newRowCount, rowCount);

            for (var i = 0; i < total; i++)
            {
                Console.Write("\x1b[K"); // 清行
                if (i < newRowCount)
                    Console.Write(lines[i]);
                if (i < total - 1)
                    Console.Write("\r\n");
            }

            rowCount = newRowCount;
            PositionCaret();
        }

        // 恢复到锚点后按（row, col）相对移动定位光标；col 按显示宽度计。
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
                else
                {
                    col += GetWidth(buffer[i]);
                }
            }

            Console.Write("\x1b8"); // 恢复光标到锚点
            if (row > 0)
                Console.Write($"\x1b[{row}B"); // 下移 row 行
            if (col > 0)
                Console.Write($"\x1b[{col}C"); // 右移 col 列
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
    /// 估算字符的终端显示宽度：全角 CJK 字符为 2 列，其余为 1 列。
    /// 与 Spectre.Console 内部 UnicodeCalculator.GetWidth 的简化等价实现。
    /// </summary>
    private static int GetWidth(char c)
    {
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
