using System.Text.RegularExpressions;

namespace InsightaAI.Agent.Memory;

/// <summary>
/// 旧版 Markdown 记忆格式的读写器。
/// 该格式仅用于兼容和迁移；SQLite Provider 不再以 Markdown 作为运行时事实源。
/// </summary>
public static class MemoryMarkdownSerializer
{
    public static string Format(MemoryEntry entry)
    {
        var tags = string.Join(", ", entry.Tags);
        var scope = entry.Scope.ToString().ToLowerInvariant();
        var project = entry.Project ?? "";
        var lastAccessed = entry.LastAccessedAt?.ToString("O") ?? "";

        return $"""
            ---
            name: {entry.Name}
            description: {entry.Description}
            type: {entry.Type.ToString().ToLowerInvariant()}
            scope: {scope}
            project: {project}
            tags: [{tags}]
            source: {entry.Source}
            created: {entry.CreatedAt:O}
            updated: {entry.UpdatedAt:O}
            last_accessed: {lastAccessed}
            access_count: {entry.AccessCount}
            ---

            {entry.Content}
            """;
    }

    public static MemoryEntry? Parse(string markdown, string id, string ownerOrProject, MemoryScope scope)
    {
        var match = Regex.Match(markdown, @"^---\s*\n(.*?)\n---\s*\n(.*)$", RegexOptions.Singleline);
        if (!match.Success)
            return null;

        var entry = new MemoryEntry
        {
            Id = id,
            Content = match.Groups[2].Value.Trim(),
            Scope = scope
        };

        if (scope == MemoryScope.Team)
            entry.Project = ownerOrProject;
        else
            entry.UserId = ownerOrProject;

        foreach (var line in match.Groups[1].Value.Split('\n'))
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0)
                continue;

            var key = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();
            switch (key)
            {
                case "name": entry.Name = value; break;
                case "description": entry.Description = value; break;
                case "type" when Enum.TryParse<MemoryType>(value, true, out var type): entry.Type = type; break;
                case "scope" when Enum.TryParse<MemoryScope>(value, true, out var parsedScope): entry.Scope = parsedScope; break;
                case "project" when !string.IsNullOrEmpty(value): entry.Project = value; break;
                case "tags":
                    entry.Tags = value.Trim('[', ']')
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(tag => tag.Trim())
                        .ToList();
                    break;
                case "source": entry.Source = value; break;
                case "created" when DateTime.TryParse(value, out var created): entry.CreatedAt = created; break;
                case "updated" when DateTime.TryParse(value, out var updated): entry.UpdatedAt = updated; break;
                case "last_accessed" when DateTime.TryParse(value, out var accessed): entry.LastAccessedAt = accessed; break;
                case "access_count" when int.TryParse(value, out var count): entry.AccessCount = count; break;
            }
        }

        return entry;
    }
}
