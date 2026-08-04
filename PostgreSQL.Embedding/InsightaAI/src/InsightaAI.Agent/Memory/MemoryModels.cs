using System.Text.Json.Serialization;

namespace InsightaAI.Agent.Memory;

/// <summary>
/// 记忆类型（参考 Claude Code 设计）
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MemoryType
{
    /// <summary>用户画像：角色、目标、职责、知识背景（始终私有）</summary>
    User,

    /// <summary>反馈：用户对工作方式的指导，包括纠正和确认（默认私有，项目级约定可为团队）</summary>
    Feedback,

    /// <summary>项目：进行中的工作、目标、缺陷、决策（强烈倾向团队）</summary>
    Project,

    /// <summary>参考：外部系统资源指针、文档位置（通常团队）</summary>
    Reference
}

/// <summary>
/// 记忆作用域
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MemoryScope
{
    /// <summary>私有：仅当前用户可见</summary>
    Private,

    /// <summary>团队：项目内所有用户共享</summary>
    Team
}

/// <summary>
/// Controls whether a memory is always supplied as stable user context or is
/// only available through task-related retrieval.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MemoryActivation
{
    OnDemand,
    Core
}

/// <summary>
/// 记忆条目
/// </summary>
public class MemoryEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("type")]
    public MemoryType Type { get; set; } = MemoryType.User;

    [JsonPropertyName("scope")]
    public MemoryScope Scope { get; set; } = MemoryScope.Private;

    [JsonPropertyName("activation")]
    public MemoryActivation Activation { get; set; } = MemoryActivation.OnDemand;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("source")]
    public string Source { get; set; } = "user_input"; // user_input, agent_inference, file_import

    [JsonPropertyName("project")]
    public string? Project { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("last_accessed_at")]
    public DateTime? LastAccessedAt { get; set; }

    [JsonPropertyName("access_count")]
    public int AccessCount { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>搜索结果的相似度分数（不持久化）</summary>
    [JsonIgnore]
    public float? RelevanceScore { get; set; }

    /// <summary>MEMORY.md 中的索引行（不持久化）</summary>
    [JsonIgnore]
    public string? IndexLine { get; set; }
}

/// <summary>
/// A stable projection of memories selected for one Agent turn.
/// </summary>
public sealed record ActiveMemorySnapshot(
    string TurnId,
    IReadOnlyList<MemoryEntry> CoreEntries,
    IReadOnlyList<MemoryEntry> ActiveEntries,
    string Index)
{
    public IReadOnlyList<MemoryEntry> Entries => CoreEntries.Concat(ActiveEntries).ToArray();

    /// <summary>
    /// Formats this frozen snapshot for injection into the dynamic system prompt.
    /// </summary>
    public string FormatAsString()
    {
        if (Entries.Count == 0)
            return Index;

        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(Index))
            sb.AppendLine(Index);

        AppendMemories("Core memories:", CoreEntries);
        AppendMemories("Task-related memories for this turn:", ActiveEntries);
        return sb.ToString().TrimEnd();

        void AppendMemories(string title, IReadOnlyList<MemoryEntry> memories)
        {
            if (memories.Count == 0)
                return;

            sb.AppendLine(title);
            foreach (var memory in memories)
            {
                sb.Append($"- [{memory.Type}] {FormatMemoryText(memory)}");
                if (memory.Tags.Count > 0)
                    sb.Append($" (tags: {string.Join(", ", memory.Tags)})");
                sb.AppendLine();
            }
        }
    }

    private static string FormatMemoryText(MemoryEntry memory)
    {
        var name = StripTypePrefix(memory.Name, memory.Type);
        var description = StripTypePrefix(memory.Description, memory.Type);

        if (string.IsNullOrWhiteSpace(description))
            return name;

        // Imported names are commonly the first, truncated portion of the description.
        // Render the complete description once instead of repeating that portion.
        if (DescriptionContainsName(description, name))
            return description;

        return string.IsNullOrWhiteSpace(name) ? description : $"{name} — {description}";
    }

    private static string StripTypePrefix(string value, MemoryType type)
    {
        var prefix = $"{type}:";
        return value.Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value.Trim()[prefix.Length..].TrimStart()
            : value.Trim();
    }

    private static bool DescriptionContainsName(string description, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var comparableName = name.TrimEnd('.', '。', ':', '：', '…').TrimEnd();
        return comparableName.Length > 0 &&
               description.StartsWith(comparableName, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 用户画像
/// </summary>
public class UserProfile
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("preferences")]
    public Dictionary<string, string> Preferences { get; set; } = [];

    [JsonPropertyName("projects")]
    public List<ProjectContext> Projects { get; set; } = [];

    [JsonPropertyName("stack")]
    public TechnicalStack Stack { get; set; } = new();

    [JsonPropertyName("style")]
    public CommunicationStyle Style { get; set; } = new();

    [JsonPropertyName("last_updated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 项目上下文
/// </summary>
public class ProjectContext
{
    [JsonPropertyName("project_name")]
    public string ProjectName { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("technologies")]
    public List<string> Technologies { get; set; } = [];

    [JsonPropertyName("conventions")]
    public Dictionary<string, string> Conventions { get; set; } = [];
}

/// <summary>
/// 技术栈
/// </summary>
public class TechnicalStack
{
    [JsonPropertyName("languages")]
    public List<string> Languages { get; set; } = [];

    [JsonPropertyName("frameworks")]
    public List<string> Frameworks { get; set; } = [];

    [JsonPropertyName("tools")]
    public List<string> Tools { get; set; } = [];

    [JsonPropertyName("preferred_os")]
    public string PreferredOS { get; set; } = "";

    [JsonPropertyName("editor")]
    public string Editor { get; set; } = "";
}

/// <summary>
/// 沟通风格
/// </summary>
public class CommunicationStyle
{
    /// <summary>详细程度: concise, detailed, balanced</summary>
    [JsonPropertyName("verbosity")]
    public string Verbosity { get; set; } = "balanced";

    /// <summary>语言偏好: zh-CN, en-US</summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "zh-CN";

    [JsonPropertyName("prefer_examples")]
    public bool PreferExamples { get; set; }

    [JsonPropertyName("prefer_step_by_step")]
    public bool PreferStepByStep { get; set; }
}

/// <summary>
/// 搜索选项
/// </summary>
public class MemorySearchOptions
{
    /// <summary>限定记忆类型</summary>
    public MemoryType? Type { get; init; }

    /// <summary>限定标签</summary>
    public List<string>? Tags { get; init; }

    /// <summary>限定项目（用于搜索团队记忆）</summary>
    public string? ProjectId { get; init; }

    /// <summary>最大返回结果数</summary>
    public int MaxResults { get; init; } = 10;

    /// <summary>最小相似度分数（用于向量搜索）</summary>
    public float MinScore { get; init; } = 0.5f;
}
