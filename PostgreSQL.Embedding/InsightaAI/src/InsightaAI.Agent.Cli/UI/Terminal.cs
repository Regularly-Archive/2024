using Spectre.Console;

namespace InsightaAI.Agent.Cli.UI;

/// <summary>
/// 终端底层操作封装。
///
/// git bash（mintty 经 winpty）下 Win32 Console 的屏幕缓冲查询（GetBufferInfo）
/// 不可用，Spectre 的 <see cref="AnsiConsole.Clear()"/> 走 LegacyConsoleBackend
/// 会因此抛出 IOException（The handle is invalid）。这里统一做安全清屏：
/// 优先 Spectre，失败时回退为 ANSI 转义序列（winpty 会把字节透传给 mintty，
/// 而 mintty 完整支持 ANSI 清屏）。
/// </summary>
public static class Terminal
{
    /// <summary>
    /// 是否处于 git bash（MSYS2/mintty 经 winpty）环境。
    /// git bash 与 MSYS2 家族启动时都会设置 <c>MSYSTEM</c> 环境变量，
    /// 是区分该环境与 Windows Terminal / ConHost / Linux 的可靠信号。
    /// </summary>
    public static bool IsGitBash =>
        Environment.GetEnvironmentVariable("MSYSTEM") is { Length: > 0 };

    /// <summary>
    /// 清空屏幕。兼容 git bash（winpty/mintty）与常规 Windows 控制台。
    /// </summary>
    public static void Clear()
    {
        try
        {
            AnsiConsole.Clear();
        }
        catch (IOException)
        {
            // LegacyConsoleBackend.Clear 内部调用 Console.CursorLeft 触发
            // GetBufferInfo → IOException。直接写 ANSI 清屏序列绕过该路径。
            Console.Write("\x1b[2J\x1b[H");
            Console.Out.Flush();
        }
    }
}
