using InsightaAI.Agent.Abstractions;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace InsightaAI.Agent.Cli.UI;

/// <summary>
/// Transient CLI presentation state for in-flight tool calls. It intentionally owns all line
/// retention and rendering policy; tools and the Agent runtime only emit raw progress updates.
/// </summary>
internal sealed class ToolProgressWindow
{
    private const int MaxLines = 6;
    private const int MaxCharacters = 2_048;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _entries = [];

    public bool HasActiveEntries
    {
        get
        {
            lock (_gate)
                return _entries.Count > 0;
        }
    }

    public void Begin(string toolCallId, string toolDisplay)
    {
        lock (_gate)
            _entries.TryAdd(toolCallId, new Entry(toolDisplay));
    }

    public void Apply(string toolCallId, ToolProgressUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_gate)
        {
            if (!_entries.TryGetValue(toolCallId, out var entry))
                return;

            switch (update.Kind)
            {
                case ToolProgressKind.Status:
                case ToolProgressKind.Heartbeat:
                    if (!string.IsNullOrWhiteSpace(update.Message))
                        entry.Status = update.Message;
                    break;

                case ToolProgressKind.Output:
                    if (!string.IsNullOrEmpty(update.Text))
                        entry.Append(update.Text);
                    break;
            }
        }
    }

    public void Complete(string toolCallId)
    {
        lock (_gate)
            _entries.Remove(toolCallId);
    }

    public IRenderable Render()
    {
        lock (_gate)
        {
            if (_entries.Count == 0)
                return new Markup(string.Empty);

            return new Rows(_entries.Values.Select(RenderEntry).ToArray());
        }
    }

    private static IRenderable RenderEntry(Entry entry)
    {
        var rows = new List<IRenderable>
        {
            new Markup($"[dim]○ {entry.ToolDisplay} · {entry.Elapsed.TotalSeconds:F0}s[/]")
        };
        var isFirstProgressLine = true;
        void AddProgressLine(string text)
        {
            var prefix = isFirstProgressLine ? "  ⎿ " : "    ";
            rows.Add(new Markup($"[dim]{prefix}{Escape(text)}[/]"));
            isFirstProgressLine = false;
        }

        if (!string.IsNullOrWhiteSpace(entry.Status))
            AddProgressLine(entry.Status);
        if (entry.CollapsedLineCount > 0)
            AddProgressLine($"… {entry.CollapsedLineCount} earlier updates collapsed");
        foreach (var line in entry.Lines)
            AddProgressLine(line);

        if (rows.Count == 1)
            AddProgressLine("Working...");

        return new Rows(rows);
    }

    private static string Escape(string text) => text.Replace("[", "[[").Replace("]", "]]", StringComparison.Ordinal);

    private sealed class Entry(string toolDisplay)
    {
        private readonly Queue<string> _lines = [];
        public string ToolDisplay { get; } = toolDisplay;
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public string? Status { get; set; }
        public int CollapsedLineCount { get; private set; }
        public IReadOnlyCollection<string> Lines => _lines;
        public TimeSpan Elapsed => DateTimeOffset.UtcNow - StartedAt;

        public void Append(string text)
        {
            var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            foreach (var line in normalized.Split('\n'))
            {
                if (line.Length == 0)
                    continue;

                _lines.Enqueue(line);
                Trim();
            }
        }

        private void Trim()
        {
            while (_lines.Count > MaxLines || _lines.Sum(line => line.Length) > MaxCharacters)
            {
                _lines.Dequeue();
                CollapsedLineCount++;
            }
        }
    }
}
