using Spectre.Console;
using System.Text;

namespace InsightaAI.Agent.Cli.UI;

/// <summary>
/// prompt 编辑器消费的输入事件。粘贴只有在输入源获得明确协议边界时才会出现。
/// </summary>
internal abstract record PromptInputEvent;

internal sealed record PromptKeyInputEvent(ConsoleKeyInfo Key) : PromptInputEvent;

internal sealed record PromptPasteInputEvent(string Text) : PromptInputEvent;

internal sealed record PromptEndOfInputEvent : PromptInputEvent;

/// <summary>
/// 终端输入适配层。上层不需要知道终端、操作系统或底层 API 的差异。
/// </summary>
internal interface IPromptInputSource : IDisposable
{
    bool IsInputAvailable { get; }

    Task<PromptInputEvent> ReadAsync(CancellationToken cancellationToken);

    void PushBack(PromptInputEvent inputEvent);
}

/// <summary>
/// 基于 Spectre Console 输入的默认适配器。
///
/// 它仅信任 bracketed-paste 协议：ESC[200~ 原文 ESC[201~。任何没有完整边界的
/// 输入均作为普通按键返回；因此快速键入和 IME 上屏不会被错误折叠为粘贴块。
/// </summary>
internal sealed class ConsolePromptInputSource : IPromptInputSource
{
    private const string BracketedPasteStart = "[200~";
    private const string BracketedPasteEnd = "\u001B[201~";

    private readonly IAnsiConsoleInput _input;
    private readonly bool _parseBracketedPaste;
    private readonly Queue<PromptInputEvent> _pending = [];

    public ConsolePromptInputSource(IAnsiConsoleInput input, bool parseBracketedPaste)
    {
        _input = input;
        _parseBracketedPaste = parseBracketedPaste;
    }

    public bool IsInputAvailable =>
        _pending.Count > 0 || (Terminal.SupportsKeyAvailable && Console.KeyAvailable);

    public async Task<PromptInputEvent> ReadAsync(CancellationToken cancellationToken)
    {
        if (_pending.TryDequeue(out var pending))
        {
            return pending;
        }

        var key = await _input.ReadKeyAsync(true, cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            return new PromptEndOfInputEvent();
        }

        if (!_parseBracketedPaste || key.Value.KeyChar != '\u001B')
        {
            return new PromptKeyInputEvent(key.Value);
        }

        var pasted = await TryReadBracketedPasteAsync(cancellationToken).ConfigureAwait(false);
        return pasted is null
            ? new PromptKeyInputEvent(key.Value)
            : new PromptPasteInputEvent(pasted);
    }

    public void PushBack(PromptInputEvent inputEvent) => _pending.Enqueue(inputEvent);

    public void Dispose()
    {
    }

    private async Task<string?> TryReadBracketedPasteAsync(CancellationToken cancellationToken)
    {
        var prefix = new List<ConsoleKeyInfo>();
        foreach (var expected in BracketedPasteStart)
        {
            var next = await _input.ReadKeyAsync(true, cancellationToken).ConfigureAwait(false);
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

        var payload = new StringBuilder();
        var payloadKeys = new List<ConsoleKeyInfo>();
        var endMatchLength = 0;
        while (true)
        {
            var next = await _input.ReadKeyAsync(true, cancellationToken).ConfigureAwait(false);
            if (next is null)
            {
                EnqueueKeys(prefix);
                EnqueueKeys(payloadKeys);
                return null;
            }

            var key = next.Value;
            payloadKeys.Add(key);
            payload.Append(key.KeyChar);

            if (key.KeyChar == BracketedPasteEnd[endMatchLength])
            {
                endMatchLength++;
                if (endMatchLength == BracketedPasteEnd.Length)
                {
                    payload.Length -= BracketedPasteEnd.Length;
                    return payload.ToString();
                }
            }
            else
            {
                endMatchLength = key.KeyChar == BracketedPasteEnd[0] ? 1 : 0;
            }
        }
    }

    private void EnqueueKeys(IEnumerable<ConsoleKeyInfo> keys)
    {
        foreach (var key in keys)
        {
            _pending.Enqueue(new PromptKeyInputEvent(key));
        }
    }
}

/// <summary>
/// 通用 VT 字符流解析器。它不设置终端模式，只要求调用方提供已经能逐字符返回 VT
/// 序列的 <see cref="TextReader"/>，因此可被 Windows、POSIX 或 SSH 的具体适配器复用。
/// </summary>
internal sealed class VtPromptInputSource : IPromptInputSource
{
    private const string BracketedPasteEnd = "\u001B[201~";

    private readonly TextReader _reader;
    private readonly Queue<PromptInputEvent> _pendingEvents = [];
    private readonly Queue<char> _pendingCharacters = [];

    public VtPromptInputSource(TextReader reader)
    {
        _reader = reader;
    }

    // TextReader 不提供可移植的非阻塞可读状态。已缓存事件仍应被 Enter 去抖逻辑看见。
    public bool IsInputAvailable => _pendingEvents.Count > 0;

    public async Task<PromptInputEvent> ReadAsync(CancellationToken cancellationToken)
    {
        if (_pendingEvents.TryDequeue(out var pending))
        {
            return pending;
        }

        var character = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
        if (character is null)
        {
            return new PromptEndOfInputEvent();
        }

        if (character != '\u001B')
        {
            return new PromptKeyInputEvent(ToConsoleKey(character.Value));
        }

        return await ReadEscapeSequenceAsync(cancellationToken).ConfigureAwait(false);
    }

    public void PushBack(PromptInputEvent inputEvent) => _pendingEvents.Enqueue(inputEvent);

    public void Dispose()
    {
    }

    private async Task<PromptInputEvent> ReadEscapeSequenceAsync(CancellationToken cancellationToken)
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
            if (!IsCsiFinal(character.Value))
            {
                continue;
            }

            return sequence.ToString() switch
            {
                "200~" => await ReadBracketedPasteAsync(cancellationToken).ConfigureAwait(false),
                "A" => new PromptKeyInputEvent(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false)),
                "B" => new PromptKeyInputEvent(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false)),
                "C" => new PromptKeyInputEvent(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false)),
                "D" => new PromptKeyInputEvent(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false)),
                "H" or "1~" => new PromptKeyInputEvent(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false)),
                "F" or "4~" => new PromptKeyInputEvent(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false)),
                "3~" => new PromptKeyInputEvent(new ConsoleKeyInfo('\0', ConsoleKey.Delete, false, false, false)),
                _ => ReturnUnknownEscapeSequence(sequence.ToString())
            };
        }
    }

    private async Task<PromptInputEvent> ReadBracketedPasteAsync(CancellationToken cancellationToken)
    {
        var pasted = new StringBuilder();
        var endMatchLength = 0;
        while (true)
        {
            var character = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
            if (character is null)
            {
                // 只有完整的起止边界才是粘贴。EOF 前未闭合时，将内容还原为普通输入。
                PushCharacters("[200~" + pasted);
                return new PromptKeyInputEvent(ToConsoleKey('\u001B'));
            }

            pasted.Append(character.Value);
            if (character == BracketedPasteEnd[endMatchLength])
            {
                endMatchLength++;
                if (endMatchLength == BracketedPasteEnd.Length)
                {
                    pasted.Length -= BracketedPasteEnd.Length;
                    return new PromptPasteInputEvent(pasted.ToString());
                }
            }
            else
            {
                endMatchLength = character == BracketedPasteEnd[0] ? 1 : 0;
            }
        }
    }

    private PromptInputEvent ReturnUnknownEscapeSequence(string sequence)
    {
        PushCharacters("[" + sequence);
        return new PromptKeyInputEvent(ToConsoleKey('\u001B'));
    }

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

    private static bool IsCsiFinal(char character) => character is >= '@' and <= '~';

    private static ConsoleKeyInfo ToConsoleKey(char character) => character switch
    {
        '\r' or '\n' => new ConsoleKeyInfo(character, ConsoleKey.Enter, false, false, false),
        '\b' => new ConsoleKeyInfo(character, ConsoleKey.Backspace, false, false, false),
        '\u007F' => new ConsoleKeyInfo(character, ConsoleKey.Backspace, false, false, false),
        '\u0003' => new ConsoleKeyInfo(
            character,
            ConsoleKey.C,
            shift: false,
            alt: false,
            control: true),
        _ => new ConsoleKeyInfo(character, ConsoleKey.NoName, false, false, false)
    };
}

internal static class PromptInputSourceFactory
{
    public static IPromptInputSource Create(IAnsiConsoleInput fallbackInput, bool bracketedPasteEnabled)
    {
        if (WindowsVtPromptInputSource.TryCreate(out var windowsVtSource))
        {
            return windowsVtSource!;
        }

        if (WindowsConsolePromptInputSource.TryCreate(out var windowsSource))
        {
            return windowsSource!;
        }

        return new ConsolePromptInputSource(fallbackInput, bracketedPasteEnabled);
    }
}
