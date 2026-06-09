using System.Text;
using System.Text.RegularExpressions;

namespace InsightaAI.Agent.Memory;

/// <summary>
/// 记忆管理器 - 提供高级记忆操作（参考 Claude Code 设计）
/// </summary>
public sealed class MemoryManager : IMemoryManager
{
    private readonly IMemoryProvider _provider;

    public MemoryManager(IMemoryProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<MemoryEntry> SaveMemoryAsync(
        string userId,
        string content,
        MemoryType? type = null,
        List<string>? tags = null,
        string? source = null,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        // 检查是否应该保存（What NOT to Save）
        if (!ShouldSaveMemory(content))
        {
            return new MemoryEntry
            {
                UserId = userId,
                Content = content,
                Type = MemoryType.User,
                Source = "filtered",
                Metadata = { ["filtered"] = "true", ["reason"] = "matches exclusion rules" }
            };
        }

        // 自动分类（如果未指定类型）
        var resolvedType = type ?? ClassifyContent(content);

        // 确定作用域
        var scope = DetermineScope(resolvedType, project);

        // 自动生成名称和描述
        var (name, description) = GenerateNameAndDescription(content, resolvedType);

        // 自动提取标签（如果未指定标签）
        var resolvedTags = tags ?? ExtractTags(content);

        // 检查是否存在相似记忆（去重）
        var existingMemory = await FindSimilarMemoryAsync(userId, content, resolvedType, project, cancellationToken);
        if (existingMemory != null)
        {
            // 更新现有记忆而非创建新记忆
            existingMemory.Content = content;
            existingMemory.Name = name;
            existingMemory.Description = description;
            existingMemory.Tags = resolvedTags;
            existingMemory.UpdatedAt = DateTime.UtcNow;

            await _provider.UpdateMemoryAsync(existingMemory, cancellationToken);
            return existingMemory;
        }

        var entry = new MemoryEntry
        {
            UserId = userId,
            Name = name,
            Description = description,
            Content = content,
            Type = resolvedType,
            Scope = scope,
            Tags = resolvedTags,
            Source = source ?? "user_input",
            Project = project
        };

        await _provider.SaveMemoryAsync(entry, cancellationToken);
        return entry;
    }

    public async Task<List<MemoryEntry>> SearchRelevantMemoriesAsync(
        string userId,
        string context,
        int maxResults = 5,
        MemoryType? type = null,
        string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var options = new MemorySearchOptions
        {
            Type = type,
            ProjectId = projectId,
            MaxResults = maxResults
        };

        return await _provider.SearchMemoriesAsync(userId, context, options, cancellationToken);
    }

    public async Task<string> GetMemoryIndexAsync(
        string userId,
        string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        // 通过接口调用，不再依赖具体实现
        return await _provider.GetMemoryIndexAsync(userId, projectId, cancellationToken);
    }

    public async Task<string> GetUserContextAsync(
        string userId,
        string? currentProject = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        // 获取用户画像
        var profile = await _provider.GetUserProfileAsync(userId, cancellationToken);
        if (profile != null)
        {
            sb.AppendLine("## User Profile");
            if (!string.IsNullOrEmpty(profile.DisplayName))
                sb.AppendLine($"- Name: {profile.DisplayName}");

            if (profile.Style != null)
            {
                sb.AppendLine($"- Language: {profile.Style.Language}");
                sb.AppendLine($"- Verbosity: {profile.Style.Verbosity}");
            }

            if (profile.Stack?.Languages is { Count: > 0 } languages)
                sb.AppendLine($"- Languages: {string.Join(", ", languages)}");

            if (profile.Stack?.Frameworks is { Count: > 0 } frameworks)
                sb.AppendLine($"- Frameworks: {string.Join(", ", frameworks)}");

            sb.AppendLine();
        }

        // 获取 MEMORY.md 索引
        var memoryIndex = await GetMemoryIndexAsync(userId, currentProject, cancellationToken);
        if (!string.IsNullOrWhiteSpace(memoryIndex))
        {
            sb.AppendLine(memoryIndex);
        }

        return sb.ToString();
    }

    public async Task UpdateUserProfileAsync(
        string userId,
        Dictionary<string, string> updates,
        CancellationToken cancellationToken = default)
    {
        var profile = await _provider.GetUserProfileAsync(userId, cancellationToken)
            ?? new UserProfile { UserId = userId };

        foreach (var (key, value) in updates)
        {
            profile.Preferences[key] = value;
        }

        await _provider.SaveUserProfileAsync(profile, cancellationToken);
    }

    public async Task<UserProfile?> GetUserProfileAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await _provider.GetUserProfileAsync(userId, cancellationToken);
    }

    public async Task SaveUserProfileAsync(
        UserProfile profile,
        CancellationToken cancellationToken = default)
    {
        await _provider.SaveUserProfileAsync(profile, cancellationToken);
    }

    #region Private Methods

    /// <summary>
    /// 判断是否应该保存记忆（What NOT to Save）
    /// 参考 Claude Code 的排除规则
    /// </summary>
    private static bool ShouldSaveMemory(string content)
    {
        var lower = content.ToLowerInvariant();

        // 1. 代码模式、约定、架构、文件路径或项目结构（整段内容主要是这类信息）
        if (Regex.IsMatch(lower, @"^(文件路径|目录结构|代码模式|file path|directory structure|代码结构)"))
            return false;

        // 2. Git 历史、近期变更或谁改了什么
        if (Regex.IsMatch(lower, @"^(git log|git blame|提交记录|commit history|git diff)"))
            return false;

        // 3. 调试方案或修复套路（如果只是临时性的）
        if (lower.Contains("调试") && lower.Contains("临时"))
            return false;

        // 4. 临时性的任务细节
        if (Regex.IsMatch(lower, @"^(临时|temporary|进行中|in progress|当前状态|wip)"))
            return false;

        // 5. 敏感数据
        if (ContainsSensitiveData(lower))
            return false;

        return true;
    }

    /// <summary>
    /// 检查是否包含敏感数据
    /// </summary>
    private static bool ContainsSensitiveData(string content)
    {
        string[] patterns =
        [
            @"api[\s_\-]?key\s*[:=]\s*\S+",
            @"password\s*[:=]\s*\S+",
            @"secret\s*[:=]\s*\S+",
            @"token\s*[:=]\s*\S+",
            @"-----begin\s+(rsa\s+)?private\s+key-----"
        ];

        return patterns.Any(p => Regex.IsMatch(content, p, RegexOptions.IgnoreCase));
    }

    /// <summary>
    /// 确定记忆作用域
    /// </summary>
    private static MemoryScope DetermineScope(MemoryType type, string? project)
    {
        return type switch
        {
            MemoryType.User => MemoryScope.Private,      // 始终私有
            MemoryType.Feedback => MemoryScope.Private,  // 默认私有
            MemoryType.Project => !string.IsNullOrEmpty(project) ? MemoryScope.Team : MemoryScope.Private,
            MemoryType.Reference => MemoryScope.Team,    // 通常团队
            _ => MemoryScope.Private
        };
    }

    /// <summary>
    /// 根据内容自动分类（参考 Claude Code 的类型）
    /// </summary>
    private static MemoryType ClassifyContent(string content)
    {
        var lower = content.ToLowerInvariant();

        // 用户画像类
        if (lower.Contains("我是") || lower.Contains("我的角色") || lower.Contains("i'm a") ||
            lower.Contains("my role") || lower.Contains("偏好") || lower.Contains("preference"))
        {
            return MemoryType.User;
        }

        // 反馈类
        if (lower.Contains("不要") || lower.Contains("停止") || lower.Contains("don't") ||
            lower.Contains("stop") || lower.Contains("以后") || lower.Contains("总是") ||
            lower.Contains("always") || lower.Contains("never"))
        {
            return MemoryType.Feedback;
        }

        // 项目类
        if (lower.Contains("项目") || lower.Contains("project") || lower.Contains("目标") ||
            lower.Contains("goal") || lower.Contains("截止") || lower.Contains("deadline") ||
            lower.Contains("决策") || lower.Contains("decision"))
        {
            return MemoryType.Project;
        }

        // 参考类
        if (lower.Contains("文档") || lower.Contains("链接") || lower.Contains("url") ||
            lower.Contains("document") || lower.Contains("reference") || lower.Contains("资源"))
        {
            return MemoryType.Reference;
        }

        // 默认为用户类
        return MemoryType.User;
    }

    /// <summary>
    /// 生成记忆名称和描述
    /// </summary>
    private static (string Name, string Description) GenerateNameAndDescription(string content, MemoryType type)
    {
        // 提取前 50 个字符作为名称基础
        var firstLine = content.Split('\n').FirstOrDefault()?.Trim() ?? "";
        var name = firstLine.Length > 50 ? firstLine[..50] + "..." : firstLine;

        // 根据类型添加前缀
        name = type switch
        {
            MemoryType.User => $"User: {name}",
            MemoryType.Feedback => $"Feedback: {name}",
            MemoryType.Project => $"Project: {name}",
            MemoryType.Reference => $"Ref: {name}",
            _ => name
        };

        // 生成描述（用于 MEMORY.md 索引）
        var description = content.Length > 100 ? content[..100] + "..." : content;
        description = description.Replace('\n', ' ').Trim();

        return (name, description);
    }

    /// <summary>
    /// 从内容中提取标签
    /// </summary>
    private static List<string> ExtractTags(string content)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lower = content.ToLowerInvariant();

        // 编程语言
        string[] languages = ["csharp", "python", "javascript", "typescript", "java", "go", "rust"];
        foreach (var lang in languages)
        {
            if (lower.Contains(lang))
                tags.Add(lang);
        }

        // 常见技术
        string[] technologies = ["docker", "kubernetes", "redis", "postgres", "mysql", "nginx", "git"];
        foreach (var tech in technologies)
        {
            if (lower.Contains(tech))
                tags.Add(tech);
        }

        // 操作类型
        if (lower.Contains("错误") || lower.Contains("error") || lower.Contains("bug"))
            tags.Add("error");
        if (lower.Contains("优化") || lower.Contains("performance"))
            tags.Add("optimization");
        if (lower.Contains("配置") || lower.Contains("config"))
            tags.Add("configuration");
        if (lower.Contains("偏好") || lower.Contains("preference"))
            tags.Add("preference");

        return tags.ToList();
    }

    /// <summary>
    /// 查找相似记忆（用于去重）
    /// </summary>
    private async Task<MemoryEntry?> FindSimilarMemoryAsync(
        string userId,
        string content,
        MemoryType type,
        string? project,
        CancellationToken cancellationToken)
    {
        // 搜索同类型的记忆
        var existingMemories = await _provider.SearchMemoriesAsync(
            userId,
            content,
            new MemorySearchOptions
            {
                Type = type,
                ProjectId = project,
                MaxResults = 10
            },
            cancellationToken);

        foreach (var memory in existingMemories)
        {
            // 检查内容相似度
            if (IsContentSimilar(content, memory.Content))
            {
                return memory;
            }
        }

        return null;
    }

    /// <summary>
    /// 判断两条记忆内容是否相似（用于去重）
    /// </summary>
    private static bool IsContentSimilar(string newContent, string existingContent)
    {
        // 标准化内容
        var normalizedNew = NormalizeContent(newContent);
        var normalizedExisting = NormalizeContent(existingContent);

        // 完全相同
        if (normalizedNew == normalizedExisting)
            return true;

        // 提取核心实体进行比较
        var newEntities = ExtractEntities(normalizedNew);
        var existingEntities = ExtractEntities(normalizedExisting);

        // 如果核心实体相同，认为是重复记忆
        if (newEntities.Count > 0 && existingEntities.Count > 0)
        {
            var commonEntities = newEntities.Intersect(existingEntities, StringComparer.OrdinalIgnoreCase).Count();
            var similarity = (double)commonEntities / Math.Max(newEntities.Count, existingEntities.Count);

            // 相似度超过 70% 认为是重复
            if (similarity >= 0.7)
                return true;
        }

        // 检查关键词重叠
        var newWords = ExtractKeywords(normalizedNew);
        var existingWords = ExtractKeywords(normalizedExisting);

        if (newWords.Count > 0 && existingWords.Count > 0)
        {
            var commonWords = newWords.Intersect(existingWords, StringComparer.OrdinalIgnoreCase).Count();
            var overlap = (double)commonWords / Math.Min(newWords.Count, existingWords.Count);

            // 关键词重叠超过 80% 认为是重复
            if (overlap >= 0.8)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 标准化内容（去除标点、空格等）
    /// </summary>
    private static string NormalizeContent(string content)
    {
        // 去除标点符号和多余空格
        var normalized = Regex.Replace(content, @"[，。！？、；：""''【】（）《》\s]+", " ");
        normalized = normalized.Trim().ToLowerInvariant();
        return normalized;
    }

    /// <summary>
    /// 提取核心实体（人名、地名、专有名词等）
    /// </summary>
    private static List<string> ExtractEntities(string content)
    {
        var entities = new List<string>();

        // 提取中文人名（2-4个汉字）
        var nameMatches = Regex.Matches(content, @"[\u4e00-\u9fa5]{2,4}(?=是|的|拥有|住在|工作)");
        foreach (Match match in nameMatches)
        {
            entities.Add(match.Value);
        }

        // 提取 URL
        var urlMatches = Regex.Matches(content, @"https?://[^\s]+");
        foreach (Match match in urlMatches)
        {
            entities.Add(match.Value);
        }

        // 提取英文专有名词（首字母大写的词）
        var properNounMatches = Regex.Matches(content, @"\b[A-Z][a-z]+\b");
        foreach (Match match in properNounMatches)
        {
            entities.Add(match.Value);
        }

        return entities.Distinct().ToList();
    }

    /// <summary>
    /// 提取关键词（去除停用词）
    /// </summary>
    private static HashSet<string> ExtractKeywords(string content)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "的", "是", "在", "了", "和", "与", "或", "不", "有", "这", "那", "我", "你", "他", "她", "它",
            "a", "an", "the", "is", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "do", "does", "did", "will", "would", "could", "should",
            "may", "might", "can", "shall", "to", "of", "in", "for", "on", "with", "at", "by", "from"
        };

        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in words)
        {
            if (word.Length >= 2 && !stopWords.Contains(word))
            {
                keywords.Add(word);
            }
        }

        return keywords;
    }

    #endregion
}
