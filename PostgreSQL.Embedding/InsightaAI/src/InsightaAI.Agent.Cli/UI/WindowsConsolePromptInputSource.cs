using System.Runtime.InteropServices;
using System.Text;

namespace InsightaAI.Agent.Cli.UI;

/// <summary>
/// Windows Console 专用输入源。
///
/// ReadConsoleInputW 返回 KEY_EVENT_RECORD，而非仅有文本的 VT 字符流，因此能同时
/// 保留 Unicode 字符、虚拟键以及 Shift/Ctrl/Alt 修饰状态。这样 Shift+Enter 与
/// Ctrl+Enter 都会到达编辑器，而 bracketed-paste 边界仍可按字符序列解析。
/// </summary>
internal sealed class WindowsConsolePromptInputSource : IPromptInputSource
{
    private const int StdInputHandle = -10;
    private const ushort KeyEvent = 0x0001;
    private const uint RightAltPressed = 0x0001;
    private const uint LeftAltPressed = 0x0002;
    private const uint RightCtrlPressed = 0x0004;
    private const uint LeftCtrlPressed = 0x0008;
    private const uint ShiftPressed = 0x0010;
    private const string BracketedPasteStart = "[200~";
    private const string BracketedPasteEnd = "\u001B[201~";

    private readonly IntPtr _inputHandle;
    private readonly Queue<PromptInputEvent> _pendingEvents = [];
    private readonly Queue<ConsoleKeyInfo> _pendingKeys = [];

    private WindowsConsolePromptInputSource(IntPtr inputHandle)
    {
        _inputHandle = inputHandle;
    }

    public bool IsInputAvailable =>
        _pendingEvents.Count > 0 || _pendingKeys.Count > 0 || HasUnreadKeyDownEvent();

    public async Task<PromptInputEvent> ReadAsync(CancellationToken cancellationToken)
    {
        if (_pendingEvents.TryDequeue(out var pending))
        {
            return pending;
        }

        var key = await ReadKeyAsync(cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            return new PromptEndOfInputEvent();
        }

        if (key.Value.KeyChar != '\u001B')
        {
            return new PromptKeyInputEvent(key.Value);
        }

        var pasted = await TryReadBracketedPasteAsync(cancellationToken).ConfigureAwait(false);
        return pasted is null
            ? new PromptKeyInputEvent(key.Value)
            : new PromptPasteInputEvent(pasted);
    }

    public void PushBack(PromptInputEvent inputEvent) => _pendingEvents.Enqueue(inputEvent);

    public void Dispose()
    {
    }

    public static bool TryCreate(out WindowsConsolePromptInputSource? source)
    {
        source = null;
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected)
        {
            return false;
        }

        var inputHandle = GetStdHandle(StdInputHandle);
        if (inputHandle == IntPtr.Zero || inputHandle == new IntPtr(-1) ||
            !GetConsoleMode(inputHandle, out _))
        {
            return false;
        }

        source = new WindowsConsolePromptInputSource(inputHandle);
        return true;
    }

    private async Task<ConsoleKeyInfo?> ReadKeyAsync(CancellationToken cancellationToken)
    {
        if (_pendingKeys.TryDequeue(out var pending))
        {
            return pending;
        }

        return await Task.Run(ReadNextKey, cancellationToken).ConfigureAwait(false);
    }

    private ConsoleKeyInfo? ReadNextKey()
    {
        while (true)
        {
            var records = new InputRecord[1];
            if (!ReadConsoleInput(_inputHandle, records, 1, out var count) || count == 0)
            {
                return null;
            }

            var record = records[0];
            if (record.EventType != KeyEvent || !record.KeyEvent.KeyDown)
            {
                continue;
            }

            var key = ToConsoleKey(record.KeyEvent);
            for (var i = 1; i < Math.Max(1, (int)record.KeyEvent.RepeatCount); i++)
            {
                _pendingKeys.Enqueue(key);
            }
            return key;
        }
    }

    private bool HasUnreadKeyDownEvent()
    {
        // GetNumberOfConsoleInputEvents 会把 KeyUp、鼠标和窗口事件也计入。若直接用它
        // 判断 Enter 后是否“空闲”，普通 Enter 的 KeyUp 会被误判为后续输入，导致无法发送。
        var records = new InputRecord[16];
        if (!PeekConsoleInput(_inputHandle, records, (uint)records.Length, out var count))
        {
            return false;
        }

        for (var i = 0; i < (int)count; i++)
        {
            if (records[i].EventType == KeyEvent && records[i].KeyEvent.KeyDown)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<string?> TryReadBracketedPasteAsync(CancellationToken cancellationToken)
    {
        var prefix = new List<ConsoleKeyInfo>();
        foreach (var expected in BracketedPasteStart)
        {
            var next = await ReadKeyAsync(cancellationToken).ConfigureAwait(false);
            if (next is null)
            {
                EnqueueKeys(prefix);
                return null;
            }

            prefix.Add(next.Value);
            if (next.Value.KeyChar != expected)
            {
                EnqueueKeys(prefix);
                return null;
            }
        }

        var pasted = new StringBuilder();
        var pastedKeys = new List<ConsoleKeyInfo>();
        var endMatchLength = 0;
        while (true)
        {
            var next = await ReadKeyAsync(cancellationToken).ConfigureAwait(false);
            if (next is null)
            {
                EnqueueKeys(prefix);
                EnqueueKeys(pastedKeys);
                return null;
            }

            pastedKeys.Add(next.Value);
            pasted.Append(next.Value.KeyChar);
            if (next.Value.KeyChar == BracketedPasteEnd[endMatchLength])
            {
                endMatchLength++;
                if (endMatchLength == BracketedPasteEnd.Length)
                {
                    pasted.Length -= BracketedPasteEnd.Length;
                    return pasted.ToString();
                }
            }
            else
            {
                endMatchLength = next.Value.KeyChar == BracketedPasteEnd[0] ? 1 : 0;
            }
        }
    }

    private void EnqueueKeys(IEnumerable<ConsoleKeyInfo> keys)
    {
        foreach (var key in keys)
        {
            _pendingKeys.Enqueue(key);
        }
    }

    private static ConsoleKeyInfo ToConsoleKey(KeyEventRecord record)
    {
        var modifiers = record.ControlKeyState;
        return new ConsoleKeyInfo(
            record.UnicodeChar,
            (ConsoleKey)record.VirtualKeyCode,
            shift: (modifiers & ShiftPressed) != 0,
            alt: (modifiers & (LeftAltPressed | RightAltPressed)) != 0,
            control: (modifiers & (LeftCtrlPressed | RightCtrlPressed)) != 0);
    }

    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    private struct InputRecord
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KeyEventRecord KeyEvent;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct KeyEventRecord
    {
        [MarshalAs(UnmanagedType.Bool)] public bool KeyDown;
        public ushort RepeatCount;
        public ushort VirtualKeyCode;
        public ushort VirtualScanCode;
        public char UnicodeChar;
        public uint ControlKeyState;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(IntPtr consoleHandle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadConsoleInput(
        IntPtr consoleInput,
        [Out] InputRecord[] buffer,
        uint length,
        out uint numberOfEventsRead);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekConsoleInput(
        IntPtr consoleInput,
        [Out] InputRecord[] buffer,
        uint length,
        out uint numberOfEventsRead);
}
