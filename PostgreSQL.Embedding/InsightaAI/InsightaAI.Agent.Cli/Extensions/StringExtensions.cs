using Spectre.Console;

namespace InsightaAI.Agent.Cli.Extensions;

/// <summary>
/// 字符串扩展方法
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// 截断字符串到指定长度，超出部分用省略号替代
    /// </summary>
    /// <param name="text">原始字符串</param>
    /// <param name="maxLength">最大长度（包含省略号）</param>
    /// <param name="ellipsis">省略号文本，默认为 "..."</param>
    /// <returns>截断后的字符串</returns>
    public static string Truncate(this string? text, int maxLength, string ellipsis = "...")
    {
        if (text == null) return string.Empty;
        if (maxLength < 0) throw new ArgumentOutOfRangeException(nameof(maxLength));

        // 如果最大长度小于省略号长度，直接截断省略号
        if (maxLength <= ellipsis.Length)
            return ellipsis[..maxLength];

        return text.Length <= maxLength
            ? text
            : text[..(maxLength - ellipsis.Length)] + ellipsis;
    }

    /// <summary>
    /// 根据控制台宽度截断字符串
    /// </summary>
    /// <param name="text">原始字符串</param>
    /// <param name="offset">左侧预留的宽度（用于缩进、图标等）</param>
    /// <param name="ellipsis">省略号文本，默认为 "..."</param>
    /// <returns>截断后的字符串</returns>
    public static string TruncateToConsoleWidth(this string? text, int offset = 0, string ellipsis = "...")
    {
        if (text == null) return string.Empty;

        var consoleWidth = GetConsoleWidth();
        var maxWidth = consoleWidth - offset;

        // 确保最小宽度
        if (maxWidth < ellipsis.Length + 5)
            maxWidth = ellipsis.Length + 5;

        return text.Truncate(maxWidth, ellipsis);
    }

    /// <summary>
    /// 获取控制台宽度
    /// </summary>
    public static int GetConsoleWidth()
    {
        try
        {
            return AnsiConsole.Console.Profile.Width;
        }
        catch
        {
            // 回退到系统控制台宽度
            return Console.WindowWidth > 0 ? Console.WindowWidth : 120;
        }
    }
}
