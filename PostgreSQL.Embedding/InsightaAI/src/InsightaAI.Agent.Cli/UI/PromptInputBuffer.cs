namespace InsightaAI.Agent.Cli.UI;

/// <summary>
/// 多行提示的结构化输入缓冲区。普通文本按字符单元保存；粘贴内容作为原子单元，
/// 屏幕上只显示摘要，提交时仍返回原始文本。
/// </summary>
internal sealed class PromptInputBuffer
{
    private readonly List<Entry> _entries = [];

    /// <summary>光标前已有的输入单元数量。</summary>
    public int Caret { get; private set; }

    public int Count => _entries.Count;

    public bool IsCaretAtEnd => Caret == _entries.Count;

    public bool ContainsPaste => _entries.Any(static entry => entry.RawText != entry.DisplayText);

    public string Text => string.Concat(_entries.Select(static entry => entry.RawText));

    public string DisplayText => string.Concat(_entries.Select(static entry => entry.DisplayText));

    public string DisplayTextBeforeCaret => string.Concat(_entries.Take(Caret).Select(static entry => entry.DisplayText));

    public void InsertText(string text) => Insert(new Entry(text, text));

    public void InsertPaste(string text)
    {
        var label = $"[pasted {text.Length:N0} characters]";
        Insert(new Entry(text, label));
    }

    public bool Backspace()
    {
        if (Caret == 0)
            return false;

        _entries.RemoveAt(--Caret);
        return true;
    }

    public bool Delete()
    {
        if (Caret >= _entries.Count)
            return false;

        _entries.RemoveAt(Caret);
        return true;
    }

    public bool MoveLeft()
    {
        if (Caret == 0)
            return false;

        Caret--;
        return true;
    }

    public bool MoveRight()
    {
        if (Caret >= _entries.Count)
            return false;

        Caret++;
        return true;
    }

    public void MoveHome()
    {
        while (Caret > 0 && !_entries[Caret - 1].DisplayText.Contains('\n'))
            Caret--;
    }

    public void MoveEnd()
    {
        while (Caret < _entries.Count && !_entries[Caret].DisplayText.Contains('\n'))
            Caret++;
    }

    private void Insert(Entry entry)
    {
        _entries.Insert(Caret++, entry);
    }

    private sealed record Entry(string RawText, string DisplayText);
}
