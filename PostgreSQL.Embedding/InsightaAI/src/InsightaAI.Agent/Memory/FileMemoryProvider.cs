using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InsightaAI.Agent.Memory;

/// <summary>
/// 基于文件系统的记忆存储提供者（参考 Claude Code 设计）
///
/// 存储结构：
/// ~/.insighta/memories/
/// ├── private/{userId}/           # 私有记忆
/// │   ├── MEMORY.md              # 索引文件（加载到 System Prompt）
/// │   ├── user-profile.md        # 用户画像
/// │   └── memories/
/// │       ├── {id}.md            # 单条记忆
/// │       └── ...
/// └── team/{projectId}/           # 团队记忆（项目级）
///     ├── MEMORY.md              # 索引文件
///     └── memories/
///         ├── {id}.md
///         └── ...
/// </summary>
public sealed class FileMemoryProvider : IMemoryProvider
{
    private readonly string _basePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// 创建文件记忆提供者
    /// </summary>
    /// <param name="basePath">存储根目录，默认 ~/.insightai/memory</param>
    public FileMemoryProvider(string? basePath = null)
    {
        _basePath = basePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".insighta",
            "memories");
    }

    /// <summary>
    /// 获取 MEMORY.md 索引内容（用于注入 System Prompt）
    /// </summary>
    public async Task<string> GetMemoryIndexAsync(
        string userId,
        string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        // 加载私有记忆索引
        var privateIndexPath = Path.Combine(GetPrivateDirectory(userId), "MEMORY.md");
        if (File.Exists(privateIndexPath))
        {
            var privateIndex = await File.ReadAllTextAsync(privateIndexPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(privateIndex))
            {
                sb.AppendLine($"## Private Memories at '{privateIndex}'");
                sb.AppendLine(privateIndex);
                sb.AppendLine();
            }
        }

        // 加载团队记忆索引（如果指定了项目）
        if (!string.IsNullOrEmpty(projectId))
        {
            var teamIndexPath = Path.Combine(GetTeamDirectory(projectId), "MEMORY.md");
            if (File.Exists(teamIndexPath))
            {
                var teamIndex = await File.ReadAllTextAsync(teamIndexPath, cancellationToken);
                if (!string.IsNullOrWhiteSpace(teamIndex))
                {
                    sb.AppendLine($"## Team Memories at '{teamIndexPath}'");
                    sb.AppendLine(teamIndex);
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 获取单条记忆详情（按需加载）
    /// </summary>
    public async Task<MemoryEntry?> GetMemoryByIdAsync(
        string userId,
        string memoryId,
        string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        // 先查私有记忆
        var privatePath = Path.Combine(GetPrivateDirectory(userId), "memories", $"{memoryId}.md");
        if (File.Exists(privatePath))
        {
            var content = await File.ReadAllTextAsync(privatePath, cancellationToken);
            return MemoryMarkdownSerializer.Parse(content, memoryId, userId, MemoryScope.Private);
        }

        // 再查团队记忆
        if (!string.IsNullOrEmpty(projectId))
        {
            var teamPath = Path.Combine(GetTeamDirectory(projectId), "memories", $"{memoryId}.md");
            if (File.Exists(teamPath))
            {
                var content = await File.ReadAllTextAsync(teamPath, cancellationToken);
                return MemoryMarkdownSerializer.Parse(content, memoryId, projectId, MemoryScope.Team);
            }
        }

        return null;
    }

    public async Task SaveMemoryAsync(MemoryEntry entry, CancellationToken cancellationToken = default)
    {
        var memoriesDir = GetMemoriesDirectory(entry);
        Directory.CreateDirectory(memoriesDir);

        // 保存记忆文件
        var filePath = Path.Combine(memoriesDir, $"{entry.Id}.md");
        var content = MemoryMarkdownSerializer.Format(entry);
        await File.WriteAllTextAsync(filePath, content, cancellationToken);

        // 更新 MEMORY.md 索引
        await UpdateMemoryIndexAsync(entry, cancellationToken);
    }

    public async Task<MemoryEntry?> GetMemoryAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_basePath))
            return null;

        // 搜索所有目录
        foreach (var scopeDir in Directory.GetDirectories(_basePath))
        {
            foreach (var userDir in Directory.GetDirectories(scopeDir))
            {
                var filePath = Path.Combine(userDir, "memories", $"{id}.md");
                if (File.Exists(filePath))
                {
                    var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                    var scope = Path.GetFileName(scopeDir) == "team" ? MemoryScope.Team : MemoryScope.Private;
                    return MemoryMarkdownSerializer.Parse(content, id, Path.GetFileName(userDir), scope);
                }
            }
        }

        return null;
    }

    public async Task<List<MemoryEntry>> SearchMemoriesAsync(
        string userId,
        string query,
        MemorySearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MemorySearchOptions();
        var results = new List<(MemoryEntry Entry, float Score)>();

        // 搜索私有记忆
        var privateMemories = await LoadMemoriesFromDirectoryAsync(
            Path.Combine(GetPrivateDirectory(userId), "memories"),
            userId, MemoryScope.Private, cancellationToken);

        foreach (var memory in privateMemories)
        {
            if (ShouldIncludeMemory(memory, options))
            {
                var score = CalculateRelevanceScore(memory, query);
                if (score > 0)
                {
                    memory.RelevanceScore = score;
                    results.Add((memory, score));
                }
            }
        }

        // 搜索团队记忆（如果指定了项目）
        if (!string.IsNullOrEmpty(options.ProjectId))
        {
            var teamMemories = await LoadMemoriesFromDirectoryAsync(
                Path.Combine(GetTeamDirectory(options.ProjectId), "memories"),
                options.ProjectId, MemoryScope.Team, cancellationToken);

            foreach (var memory in teamMemories)
            {
                if (ShouldIncludeMemory(memory, options))
                {
                    var score = CalculateRelevanceScore(memory, query);
                    if (score > 0)
                    {
                        memory.RelevanceScore = score;
                        results.Add((memory, score));
                    }
                }
            }
        }

        // 注意：搜索操作不应修改数据（CQRS 原则）
        // 访问跟踪由单独的 TouchMemoryAsync 方法处理
        return results
            .OrderByDescending(r => r.Score)
            .Take(options.MaxResults)
            .Select(r => r.Entry)
            .ToList();
    }

    /// <summary>
    /// 更新记忆的访问信息（用于跟踪访问频率）
    /// </summary>
    public async Task TouchMemoryAsync(string id, CancellationToken cancellationToken = default)
    {
        var memory = await GetMemoryAsync(id, cancellationToken);
        if (memory != null)
        {
            memory.LastAccessedAt = DateTime.UtcNow;
            memory.AccessCount++;
            await UpdateMemoryAsync(memory, cancellationToken);
        }
    }

    public async Task<List<MemoryEntry>> ListMemoriesAsync(
        string userId,
        MemoryType? type = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var memories = await LoadMemoriesFromDirectoryAsync(
            Path.Combine(GetPrivateDirectory(userId), "memories"),
            userId, MemoryScope.Private, cancellationToken);

        var query = memories.AsEnumerable();
        if (type.HasValue)
            query = query.Where(m => m.Type == type.Value);

        return query
            .OrderByDescending(m => m.UpdatedAt)
            .Skip(skip)
            .Take(take)
            .ToList();
    }

    public async Task UpdateMemoryAsync(MemoryEntry entry, CancellationToken cancellationToken = default)
    {
        entry.UpdatedAt = DateTime.UtcNow;
        await SaveMemoryAsync(entry, cancellationToken);
    }

    public async Task DeleteMemoryAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_basePath))
            return;

        // 搜索并删除
        foreach (var scopeDir in Directory.GetDirectories(_basePath))
        {
            foreach (var userDir in Directory.GetDirectories(scopeDir))
            {
                var filePath = Path.Combine(userDir, "memories", $"{id}.md");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);

                    // 更新索引
                    await RemoveFromMemoryIndexAsync(userDir, id, cancellationToken);
                    return;
                }
            }
        }
    }

    public async Task<UserProfile?> GetUserProfileAsync(string userId, CancellationToken cancellationToken = default)
    {
        var profilePath = Path.Combine(GetPrivateDirectory(userId), "user-profile.json");
        if (!File.Exists(profilePath))
            return null;

        var json = await File.ReadAllTextAsync(profilePath, cancellationToken);
        return JsonSerializer.Deserialize<UserProfile>(json, JsonOptions);
    }

    public async Task SaveUserProfileAsync(UserProfile profile, CancellationToken cancellationToken = default)
    {
        var userDir = GetPrivateDirectory(profile.UserId);
        Directory.CreateDirectory(userDir);

        profile.LastUpdated = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(
            Path.Combine(userDir, "user-profile.json"),
            json,
            cancellationToken);
    }

    #region Private Methods

    private string GetPrivateDirectory(string userId)
    {
        var safeUserId = Regex.Replace(userId, @"[^\w\-]", "_");
        return Path.Combine(_basePath, "private", safeUserId);
    }

    private string GetTeamDirectory(string projectId)
    {
        var safeProjectId = Regex.Replace(projectId, @"[^\w\-]", "_");
        return Path.Combine(_basePath, "team", safeProjectId);
    }

    private string GetMemoriesDirectory(MemoryEntry entry)
    {
        if (entry.Scope == MemoryScope.Team && !string.IsNullOrEmpty(entry.Project))
        {
            return Path.Combine(GetTeamDirectory(entry.Project), "memories");
        }
        return Path.Combine(GetPrivateDirectory(entry.UserId), "memories");
    }

    private string GetBaseDirectory(MemoryEntry entry)
    {
        if (entry.Scope == MemoryScope.Team && !string.IsNullOrEmpty(entry.Project))
        {
            return GetTeamDirectory(entry.Project);
        }
        return GetPrivateDirectory(entry.UserId);
    }

    private async Task UpdateMemoryIndexAsync(MemoryEntry entry, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var baseDir = GetBaseDirectory(entry);
            var indexPath = Path.Combine(baseDir, "MEMORY.md");

            // 读取现有索引
            var lines = new List<string>();
            if (File.Exists(indexPath))
            {
                lines = (await File.ReadAllLinesAsync(indexPath, cancellationToken)).ToList();
            }

            // 生成索引行，限制长度在 150 字符以内
            var description = entry.Description;
            var maxDescLength = 150 - entry.Name.Length - 20; // 预留格式字符
            if (description.Length > maxDescLength)
            {
                description = description[..maxDescLength] + "...";
            }
            var indexLine = $"- [{entry.Name}]({entry.Id}.md) — {description}";

            // 查找并更新或添加
            var existingIndex = lines.FindIndex(l => l.Contains($"({entry.Id}.md)"));
            if (existingIndex >= 0)
            {
                lines[existingIndex] = indexLine;
            }
            else
            {
                lines.Add(indexLine);
            }

            // 保持索引在 200 行以内
            if (lines.Count > 200)
            {
                lines = lines.Take(200).ToList();
            }

            await File.WriteAllLinesAsync(indexPath, lines, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task RemoveFromMemoryIndexAsync(string baseDir, string id, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var indexPath = Path.Combine(baseDir, "MEMORY.md");
            if (!File.Exists(indexPath))
                return;

            var lines = (await File.ReadAllLinesAsync(indexPath, cancellationToken)).ToList();
            lines.RemoveAll(l => l.Contains($"({id}.md)"));

            await File.WriteAllLinesAsync(indexPath, lines, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<MemoryEntry>> LoadMemoriesFromDirectoryAsync(
        string memoriesDir,
        string ownerOrProject,
        MemoryScope scope,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(memoriesDir))
            return [];

        var memories = new List<MemoryEntry>();
        foreach (var file in Directory.GetFiles(memoriesDir, "*.md"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                var id = Path.GetFileNameWithoutExtension(file);
                var memory = MemoryMarkdownSerializer.Parse(content, id, ownerOrProject, scope);
                if (memory != null)
                {
                    memories.Add(memory);
                }
            }
            catch
            {
                // 跳过无法解析的文件
            }
        }

        return memories;
    }

    private static bool ShouldIncludeMemory(MemoryEntry memory, MemorySearchOptions options)
    {
        if (options.Type.HasValue && memory.Type != options.Type.Value)
            return false;

        if (options.Tags is { Count: > 0 } tags)
        {
            if (!tags.Any(t => memory.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                return false;
        }

        return true;
    }

    private static float CalculateRelevanceScore(MemoryEntry memory, string query)
    {
        var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var content = memory.Content.ToLowerInvariant();
        var name = memory.Name.ToLowerInvariant();
        var description = memory.Description.ToLowerInvariant();
        var tags = string.Join(" ", memory.Tags).ToLowerInvariant();
        var allText = $"{name} {description} {content} {tags}";

        int matchCount = 0;
        foreach (var term in queryTerms)
        {
            if (allText.Contains(term, StringComparison.OrdinalIgnoreCase))
                matchCount++;
        }

        if (matchCount == 0)
            return 0;

        // 基础分数：匹配词数 / 总词数
        var baseScore = (float)matchCount / queryTerms.Length;

        // 名称匹配加成
        var nameBoost = queryTerms.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase)) ? 0.3f : 0;

        // 标签匹配加成
        var tagBoost = queryTerms.Any(t => tags.Contains(t, StringComparison.OrdinalIgnoreCase)) ? 0.2f : 0;

        // 最近访问加成
        var recencyBoost = memory.LastAccessedAt.HasValue
            ? Math.Max(0, 0.1f - (float)(DateTime.UtcNow - memory.LastAccessedAt.Value).TotalDays / 100)
            : 0;

        return Math.Min(1.0f, baseScore + nameBoost + tagBoost + recencyBoost);
    }

    #endregion
}
