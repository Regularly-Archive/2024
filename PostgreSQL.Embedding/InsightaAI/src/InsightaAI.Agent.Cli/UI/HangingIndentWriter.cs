using Spectre.Console;

namespace InsightaAI.Agent.Cli.UI;

/// <summary>
/// Writes a prefixed first line and indents subsequent explicit lines without changing the
/// underlying message text. Terminal soft wrapping remains untouched so copied output does not
/// gain artificial hard line breaks.
/// </summary>
internal sealed class HangingIndentWriter(
    IAnsiConsole console,
    string prefixMarkup,
    string continuationIndent)
{
    private bool _pendingCarriageReturn;

    public bool HasStarted { get; private set; }

    public bool IsAtLineStart { get; private set; }

    public void EnsureStarted()
    {
        if (HasStarted)
        {
            return;
        }

        HasStarted = true;
        IsAtLineStart = false;
        console.Markup(prefixMarkup);
    }

    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        EnsureStarted();
        var output = new System.Text.StringBuilder(text.Length + continuationIndent.Length);

        foreach (var character in text)
        {
            if (_pendingCarriageReturn)
            {
                AppendLineBreak(output);
                _pendingCarriageReturn = false;
                if (character == '\n')
                {
                    continue;
                }
            }

            if (character == '\r')
            {
                _pendingCarriageReturn = true;
                continue;
            }

            if (character == '\n')
            {
                AppendLineBreak(output);
                continue;
            }

            if (IsAtLineStart)
            {
                output.Append(continuationIndent);
                IsAtLineStart = false;
            }

            output.Append(character);
        }

        if (output.Length > 0)
        {
            console.Write(output.ToString());
        }
    }

    public void EnsureLineBreak()
    {
        if (_pendingCarriageReturn)
        {
            WriteLineBreak();
            _pendingCarriageReturn = false;
        }

        if (HasStarted && !IsAtLineStart)
        {
            WriteLineBreak();
        }
    }

    public void Reset()
    {
        _pendingCarriageReturn = false;
        HasStarted = false;
        IsAtLineStart = false;
    }

    private void WriteLineBreak()
    {
        console.WriteLine();
        IsAtLineStart = true;
    }

    private void AppendLineBreak(System.Text.StringBuilder output)
    {
        output.Append(Environment.NewLine);
        IsAtLineStart = true;
    }
}
