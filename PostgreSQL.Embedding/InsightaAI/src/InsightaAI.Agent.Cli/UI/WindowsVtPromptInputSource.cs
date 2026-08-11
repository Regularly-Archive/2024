using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace InsightaAI.Agent.Cli.UI;

/// <summary>
/// Windows VT input source. Bracketed paste supplies exact paste boundaries, while Win32 input
/// mode serializes KEY_EVENT_RECORD data so Shift/Ctrl/Alt state survives in the VT stream.
/// </summary>
internal sealed class WindowsVtPromptInputSource : IPromptInputSource
{
    private const int StdInputHandle = -10;
    private const uint EnableProcessedInput = 0x0001;
    private const uint EnableLineInput = 0x0002;
    private const uint EnableEchoInput = 0x0004;
    private const uint EnableVirtualTerminalInput = 0x0200;
    private const uint RightAltPressed = 0x0001;
    private const uint LeftAltPressed = 0x0002;
    private const uint RightCtrlPressed = 0x0004;
    private const uint LeftCtrlPressed = 0x0008;
    private const uint ShiftPressed = 0x0010;
    private const string EnableWin32InputMode = "\u001B[?9001h";
    private const string DisableWin32InputMode = "\u001B[?9001l";
    private readonly IntPtr _inputHandle;
    private readonly uint _originalInputMode;
    private readonly StreamReader _reader;
    private readonly Queue<PromptInputEvent> _pendingEvents = [];
    private readonly Queue<char> _pendingCharacters = [];
    private bool _disposed;

    private WindowsVtPromptInputSource(IntPtr inputHandle, uint originalInputMode)
    {
        _inputHandle = inputHandle;
        _originalInputMode = originalInputMode;
        _reader = new StreamReader(
            Console.OpenStandardInput(),
            Console.InputEncoding,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 256,
            leaveOpen: true);

        Console.Write(EnableWin32InputMode);
        Console.Out.Flush();
    }

    public bool IsInputAvailable => _pendingEvents.Count > 0 || _pendingCharacters.Count > 0;

    public async Task<PromptInputEvent> ReadAsync(CancellationToken cancellationToken)
    {
        if (_pendingEvents.TryDequeue(out var pending))
        {
            return pending;
        }

        while (true)
        {
            var character = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
            if (character is null)
            {
                return new PromptEndOfInputEvent();
            }

            if (character != '\u001B')
            {
                return new PromptKeyInputEvent(ToConsoleKey(character.Value));
            }

            var parsed = await ReadEscapeSequenceAsync(cancellationToken).ConfigureAwait(false);
            if (parsed is not null)
            {
                return parsed;
            }
        }
    }

    public void PushBack(PromptInputEvent inputEvent) => _pendingEvents.Enqueue(inputEvent);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Console.Write(DisableWin32InputMode);
        Console.Out.Flush();
        SetConsoleMode(_inputHandle, _originalInputMode);
        _reader.Dispose();
    }

    public static bool TryCreate(out WindowsVtPromptInputSource? source)
    {
        source = null;
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected)
        {
            return false;
        }

        var inputHandle = GetStdHandle(StdInputHandle);
        if (inputHandle == IntPtr.Zero || inputHandle == new IntPtr(-1) ||
            !GetConsoleMode(inputHandle, out var originalMode))
        {
            return false;
        }

        var vtMode = (originalMode & ~(EnableProcessedInput | EnableLineInput | EnableEchoInput)) |
                     EnableVirtualTerminalInput;
        if (!SetConsoleMode(inputHandle, vtMode))
        {
            return false;
        }

        source = new WindowsVtPromptInputSource(inputHandle, originalMode);
        return true;
    }

    private async Task<PromptInputEvent?> ReadEscapeSequenceAsync(CancellationToken cancellationToken)
    {
        var next = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
        if (next != '[')
        {
            if (next is not null)
            {
                _pendingCharacters.Enqueue(next.Value);
            }
            return new PromptKeyInputEvent(ToConsoleKey('\u001B'));
        }

        var sequence = new StringBuilder();
        while (true)
        {
            var character = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
            if (character is null)
            {
                PushCharacters("[" + sequence);
                return new PromptKeyInputEvent(ToConsoleKey('\u001B'));
            }

            sequence.Append(character.Value);
            if (character.Value is < '@' or > '~')
            {
                continue;
            }

            var value = sequence.ToString();
            if (value == "200~")
            {
                return await ReadBracketedPasteAsync(cancellationToken).ConfigureAwait(false);
            }

            if (value.EndsWith('_') &&
                TryParseWin32Key(value, out var key, out var keyDown, out var repeatCount))
            {
                if (keyDown)
                {
                    for (var i = 1; i < repeatCount; i++)
                    {
                        _pendingEvents.Enqueue(new PromptKeyInputEvent(key));
                    }
                }
                return keyDown ? new PromptKeyInputEvent(key) : null;
            }

            return ParseOrdinaryCsi(value);
        }
    }

    private async Task<PromptInputEvent> ReadBracketedPasteAsync(CancellationToken cancellationToken)
    {
        var pasted = new StringBuilder();
        var rawPasted = new StringBuilder();
        while (true)
        {
            var character = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
            if (character is null)
            {
                // Only a complete start/end pair is a paste. Restore the exact original stream
                // when EOF interrupts the block, including any Win32 sequences already decoded.
                PushCharacters("[200~" + rawPasted);
                return new PromptKeyInputEvent(ToConsoleKey('\u001B'));
            }

            rawPasted.Append(character.Value);
            if (character != '\u001B')
            {
                pasted.Append(character.Value);
                continue;
            }

            var next = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
            if (next is null)
            {
                PushCharacters("[200~" + rawPasted);
                return new PromptKeyInputEvent(ToConsoleKey('\u001B'));
            }

            rawPasted.Append(next.Value);
            if (next != '[')
            {
                pasted.Append('\u001B').Append(next.Value);
                continue;
            }

            var sequence = new StringBuilder();
            while (true)
            {
                var sequenceCharacter = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
                if (sequenceCharacter is null)
                {
                    PushCharacters("[200~" + rawPasted);
                    return new PromptKeyInputEvent(ToConsoleKey('\u001B'));
                }

                rawPasted.Append(sequenceCharacter.Value);
                sequence.Append(sequenceCharacter.Value);
                if (sequenceCharacter.Value is >= '@' and <= '~')
                {
                    break;
                }
            }

            var value = sequence.ToString();
            if (value == "201~")
            {
                return new PromptPasteInputEvent(pasted.ToString());
            }

            if (value.EndsWith('_') &&
                TryParseWin32Key(value, out var key, out var keyDown, out var repeatCount))
            {
                // In Win32 input mode, terminals may encode pasted control characters (notably
                // line breaks) as KEY_EVENT_RECORD sequences inside the bracketed-paste block.
                // Keep translated characters from KeyDown only; KeyUp carries no new text.
                if (keyDown && key.KeyChar != '\0')
                {
                    pasted.Append(key.KeyChar, repeatCount);
                }
                continue;
            }

            // The sequence is user content rather than Win32 input metadata. Preserve it exactly.
            pasted.Append("\u001B[").Append(value);
        }
    }

    private static bool TryParseWin32Key(
        string sequence,
        out ConsoleKeyInfo key,
        out bool keyDown,
        out int repeatCount)
    {
        key = default;
        keyDown = false;
        repeatCount = 1;
        var fields = sequence[..^1].Split(';');
        if (fields.Length != 6 || !fields.All(TryParseUnsigned))
        {
            return false;
        }

        var values = fields.Select(ParseUnsigned).ToArray();
        if (values[0] > byte.MaxValue || values[2] > char.MaxValue || values[5] > int.MaxValue)
        {
            return false;
        }

        var controlState = values[4];
        keyDown = values[3] != 0;
        repeatCount = Math.Max(1, (int)values[5]);
        key = new ConsoleKeyInfo(
            (char)values[2],
            (ConsoleKey)values[0],
            shift: (controlState & ShiftPressed) != 0,
            alt: (controlState & (LeftAltPressed | RightAltPressed)) != 0,
            control: (controlState & (LeftCtrlPressed | RightCtrlPressed)) != 0);
        return true;

        static bool TryParseUnsigned(string value) =>
            uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

        static uint ParseUnsigned(string value) =>
            uint.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private PromptInputEvent ParseOrdinaryCsi(string sequence) => sequence switch
    {
        "A" => Key(ConsoleKey.UpArrow),
        "B" => Key(ConsoleKey.DownArrow),
        "C" => Key(ConsoleKey.RightArrow),
        "D" => Key(ConsoleKey.LeftArrow),
        "H" or "1~" => Key(ConsoleKey.Home),
        "F" or "4~" => Key(ConsoleKey.End),
        "3~" => Key(ConsoleKey.Delete),
        _ => ReturnUnknownEscapeSequence(sequence)
    };

    private PromptInputEvent ReturnUnknownEscapeSequence(string sequence)
    {
        PushCharacters("[" + sequence);
        return new PromptKeyInputEvent(ToConsoleKey('\u001B'));
    }

    private static PromptInputEvent Key(ConsoleKey key) =>
        new PromptKeyInputEvent(new ConsoleKeyInfo('\0', key, false, false, false));

    private async Task<char?> ReadCharacterAsync(CancellationToken cancellationToken)
    {
        if (_pendingCharacters.TryDequeue(out var pending))
        {
            return pending;
        }

        var buffer = new char[1];
        var count = await _reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        return count == 0 ? null : buffer[0];
    }

    private void PushCharacters(string text)
    {
        foreach (var character in text)
        {
            _pendingCharacters.Enqueue(character);
        }
    }

    private static ConsoleKeyInfo ToConsoleKey(char character) => character switch
    {
        '\r' or '\n' => new ConsoleKeyInfo(character, ConsoleKey.Enter, false, false, false),
        '\b' or '\u007F' => new ConsoleKeyInfo(character, ConsoleKey.Backspace, false, false, false),
        '\u0003' => new ConsoleKeyInfo(character, ConsoleKey.C, false, false, true),
        _ => new ConsoleKeyInfo(character, ConsoleKey.NoName, false, false, false)
    };

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(IntPtr consoleHandle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(IntPtr consoleHandle, uint mode);
}
