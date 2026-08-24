# InsightaAI Agent - Memory System Design Document

## 1. Background & Problem

Currently, the agent lacks persistent memory capabilities:

- **No long-term memory**: Each session starts fresh, losing context from previous interactions
- **No user preferences**: Cannot learn user's coding style, project conventions, or preferences
- **No knowledge accumulation**: Repeatedly asks for the same information across sessions
- **Limited context**: Cannot reference past solutions, decisions, or project history

**Reference**: Hermes Agent implements a memory system with:
- `MEMORY.md` for factual knowledge
- `USER.md` for user preferences
- `SKILL.md` for reusable workflows
- External storage providers (SQLite, PostgreSQL, custom)

## 2. Design Goals

1. **Persistent Memory**: Store and retrieve information across sessions
2. **Full-text Retrieval**: Find relevant memories with SQLite FTS5 lexical retrieval; vector semantic search remains a future capability
3. **Memory Types**: Distinguish between user profile, feedback, project, and reference memories
4. **Storage Flexibility**: Support file-based and database storage
5. **Integration**: Seamlessly integrate with existing context compression and skill systems
6. **Performance**: Fast retrieval with minimal latency

## 3. Architecture Overview

> **当前实现状态（2026-08）**：主存储已从文件迁移到 SQLite + FTS5。`FileMemoryProvider`
> 保留作迁移与兼容实现；`PostgresMemoryProvider` 与向量搜索为早期设计目标，尚未实现。

```
Memory System
├── IMemoryProvider                    # Storage abstraction
│   ├── SqliteMemoryProvider           # SQLite + FTS5 (trigram) — 运行时主存储
│   └── FileMemoryProvider             # Markdown 文件存储（迁移/兼容保留）
├── MemoryManager                      # Core memory operations
│   ├── SaveMemoryAsync()              # Store new memory
│   ├── SearchRelevantMemoriesAsync()  # FTS 召回 + 排序（词法为主，访问频率 ≤10%、近期 ≤5% 修正）
│   ├── CreateActiveMemorySnapshotAsync() # 每 Turn 创建不可变快照（Core + Active）
│   ├── RecordMemoryAccessAsync()      # 粗粒度访问计数
│   └── GetUserProfileAsync()          # Get user preferences
├── Memory Tools
│   ├── save_memory                    # Tool: save memory
│   ├── search_memory                  # Tool: search memories
│   ├── update_memory                  # Tool: update memory
│   ├── delete_memory                  # Tool: delete memory
│   └── get_user_profile               # Tool: get user context
└── Memory Injection                   # Inject into system prompt
    └── ActiveMemorySnapshot.FormatAsString()  # 快照格式化（Agent 不承担展示逻辑）
```

## 4. Data Models

### 4.1 Memory Entry

> **当前实现状态（2026-08）**：`MemoryType` 已从 Fact/Procedure/Context/Reference 演变为
> User/Feedback/Project/Reference（参考 Claude Code 设计）；`MemoryEntry` 新增
> Name/Description/Scope/Activation/Project 字段，并引入 `ActiveMemorySnapshot` 不可变快照。

```csharp
public enum MemoryType
{
    User,        // 用户画像：角色、目标、职责、知识背景（始终私有）
    Feedback,    // 反馈：用户对工作方式的指导，包括纠正和确认（默认私有）
    Project,     // 项目：进行中的工作、目标、缺陷、决策（倾向团队）
    Reference    // 参考：外部系统资源指针、文档位置（通常团队）
}

public enum MemoryScope
{
    Private,     // 私有：仅当前用户可见
    Team         // 团队：项目内所有用户共享
}

public enum MemoryActivation
{
    OnDemand,    // 仅通过任务相关检索获得
    Core         // 始终作为稳定上下文注入（Core Entries）
}

public class MemoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string UserId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Content { get; set; }
    public MemoryType Type { get; set; } = MemoryType.User;
    public MemoryScope Scope { get; set; } = MemoryScope.Private;
    public MemoryActivation Activation { get; set; } = MemoryActivation.OnDemand;
    public List<string> Tags { get; set; } = [];
    public string Source { get; set; }  // "user_input", "agent_inference", "file_import"
    public string? Project { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAccessedAt { get; set; }
    public int AccessCount { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
    [JsonIgnore] public float? RelevanceScore { get; set; }  // 搜索分数（不持久化）
    [JsonIgnore] public string? IndexLine { get; set; }       // MEMORY.md 索引行（不持久化）
}

/// <summary>
/// 一个 Turn 内冻结的记忆投影：CoreEntries 每轮常驻，ActiveEntries 按当前输入召回。
/// 同一 Turn 的所有 LLM Round 复用同一不可变快照。
/// </summary>
public sealed record ActiveMemorySnapshot(
    string TurnId,
    IReadOnlyList<MemoryEntry> CoreEntries,
    IReadOnlyList<MemoryEntry> ActiveEntries,
    string Index)
{
    public IReadOnlyList<MemoryEntry> Entries => CoreEntries.Concat(ActiveEntries).ToArray();
    public string FormatAsString();
}
```

**`ActiveMemorySnapshot.FormatAsString()` 输出规则**：
- 先输出 `Index`（由 Provider 生成的记忆可用性索引；File Provider 为 `MEMORY.md` 文本，SQLite Provider 为记忆数量和 `search_memory` 提示）
- 再按 `Core memories:` / `Task-related memories for this turn:` 分组标题输出，条目格式 `- [Type] text (tags: ...)`
- 名称去重：当 `Name` 只是 `Description` 的截断前缀时，只输出完整 `Description`，避免重复
- `Entries` 为空时仅返回 `Index`

### 4.2 User Profile

```csharp
public class UserProfile
{
    public string UserId { get; set; }
    public string DisplayName { get; set; }
    public Dictionary<string, string> Preferences { get; set; } = new();
    public List<ProjectContext> Projects { get; set; } = new();
    public TechnicalStack Stack { get; set; } = new();
    public CommunicationStyle Style { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

public class ProjectContext
{
    public string ProjectName { get; set; }
    public string Path { get; set; }
    public string Description { get; set; }
    public List<string> Technologies { get; set; } = new();
    public Dictionary<string, string> Conventions { get; set; } = new();
}

public class TechnicalStack
{
    public List<string> Languages { get; set; } = new();
    public List<string> Frameworks { get; set; } = new();
    public List<string> Tools { get; set; } = new();
    public string PreferredOS { get; set; }
    public string Editor { get; set; }
}

public class CommunicationStyle
{
    public string Verbosity { get; set; }  // "concise", "detailed", "balanced"
    public string Language { get; set; }   // "zh-CN", "en-US"
    public bool PreferExamples { get; set; }
    public bool PreferStepByStep { get; set; }
}
```

## 5. Interface Definitions

### 5.1 Memory Provider

> **当前实现状态（2026-08）**：`SearchMemoriesAsync` 签名已从 `(userId, query, type, maxResults)`
> 改为 `(userId, query, MemorySearchOptions? options)`；新增 `ListCoreMemoriesAsync`、
> `TouchMemoryAsync`、`GetMemoryIndexAsync`。

```csharp
public interface IMemoryProvider
{
    Task SaveMemoryAsync(MemoryEntry entry, CancellationToken cancellationToken = default);

    Task<MemoryEntry?> GetMemoryAsync(string id, CancellationToken cancellationToken = default);

    Task<List<MemoryEntry>> SearchMemoriesAsync(
        string userId,
        string query,
        MemorySearchOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<List<MemoryEntry>> ListMemoriesAsync(
        string userId,
        MemoryType? type = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>返回显式提升为稳定核心上下文的私有记忆。</summary>
    Task<List<MemoryEntry>> ListCoreMemoriesAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task UpdateMemoryAsync(MemoryEntry entry, CancellationToken cancellationToken = default);

    Task DeleteMemoryAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>记录记忆被实际使用。搜索候选本身不应调用此方法。</summary>
    Task TouchMemoryAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>获取 Provider 生成的记忆可用性索引（用于注入 System Prompt）。</summary>
    Task<string> GetMemoryIndexAsync(
        string userId,
        string? projectId = null,
        CancellationToken cancellationToken = default);

    Task<UserProfile?> GetUserProfileAsync(string userId, CancellationToken cancellationToken = default);

    Task SaveUserProfileAsync(UserProfile profile, CancellationToken cancellationToken = default);
}

public class MemorySearchOptions
{
    public MemoryType? Type { get; init; }
    public List<string>? Tags { get; init; }
    public string? ProjectId { get; init; }
    public int MaxResults { get; init; } = 10;
    public float MinScore { get; init; } = 0.5f;  // 预留向量搜索最小相似度
}
```

### 5.2 Memory Manager

> **当前实现状态（2026-08）**：新增 `update_memory` / `delete_memory` 能力（
> `UpdateMemoryAsync` / `DeleteMemoryAsync`）；新增 `RecordMemoryAccessAsync` 与
> `CreateActiveMemorySnapshotAsync`；`SearchRelevantMemoriesAsync` 内部先召回 4 倍候选，
> 再按 `GetMemoryRank`（词法相关 × (1 + 10% 频率 + 5% 近期)）排序后截取。

```csharp
public interface IMemoryManager
{
    /// <summary>保存记忆（自动分类和打标签）</summary>
    Task<MemoryEntry> SaveMemoryAsync(
        string userId,
        string content,
        MemoryType? type = null,
        List<string>? tags = null,
        string? source = null,
        string? project = null,
        MemoryActivation activation = MemoryActivation.OnDemand,
        CancellationToken cancellationToken = default);

    /// <summary>更新记忆内容（按 memoryId）</summary>
    Task<bool> UpdateMemoryAsync(
        string userId,
        string memoryId,
        string? content = null,
        MemoryType? type = null,
        List<string>? tags = null,
        MemoryActivation? activation = null,
        CancellationToken cancellationToken = default);

    /// <summary>删除记忆</summary>
    Task<bool> DeleteMemoryAsync(
        string userId,
        string memoryId,
        CancellationToken cancellationToken = default);

    /// <summary>智能搜索（FTS 召回 + 排序）</summary>
    Task<List<MemoryEntry>> SearchRelevantMemoriesAsync(
        string userId,
        string context,
        int maxResults = 5,
        MemoryType? type = null,
        string? projectId = null,
        CancellationToken cancellationToken = default);

    /// <summary>记录一次粗粒度的记忆使用</summary>
    Task RecordMemoryAccessAsync(
        string memoryId,
        CancellationToken cancellationToken = default);

    /// <summary>为一个用户 Turn 创建活跃记忆快照（Core + Active）</summary>
    Task<ActiveMemorySnapshot> CreateActiveMemorySnapshotAsync(
        string userId,
        string input,
        string turnId,
        string? projectId = null,
        CancellationToken cancellationToken = default);

    Task<string> GetMemoryIndexAsync(
        string userId,
        string? projectId = null,
        CancellationToken cancellationToken = default);

    Task<string> GetUserContextAsync(
        string userId,
        string? currentProject = null,
        CancellationToken cancellationToken = default);

    Task UpdateUserProfileAsync(
        string userId,
        Dictionary<string, string> updates,
        CancellationToken cancellationToken = default);

    Task<UserProfile?> GetUserProfileAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task SaveUserProfileAsync(
        UserProfile profile,
        CancellationToken cancellationToken = default);
}
```

## 6. Storage Implementations

### 6.1 File Memory Provider

> **当前实现状态（2026-08）**：保留作迁移与兼容实现，不再是运行时主存储。
> 存储路径为 `~/.insighta/memories/private/{userId}/`（注意是 `.insighta` 而非 `.insightai`）。

存储位置：`~/.insighta/memories/private/{userId}/`（兼容实现，参考 Claude Code 设计）

```
~/.insighta/memories/
├── private/{userId}/    # 私有记忆
│   ├── MEMORY.md        # 索引文件（注入 System Prompt）
│   ├── user-profile.md  # 用户画像
│   └── memories/
│       ├── {id}.md      # 单条记忆（YAML front-matter + 正文）
│       └── ...
└── team/{projectId}/    # 团队记忆（项目级）
    ├── MEMORY.md
    └── memories/
```

**记忆文件格式**：
```markdown
---
id: 1a2b3c4d5e6f
name: 记忆名称
description: 简短描述
type: user            # user | feedback | project | reference
tags: [preference, coding-style]
source: user_input
activation: on_demand # on_demand | core
created: 2026-08-01T10:30:00Z
---
记忆正文内容
```

**优点**：人类可读、易于手动编辑、版本控制友好、无外部依赖。
**缺点**：全量扫描、无全文索引，记忆量大时性能差、上下文膨胀。

### 6.2 SQLite Memory Provider（运行时主存储）

> **当前实现状态（2026-08-04）**：SQLite + FTS5（trigram tokenizer）召回候选，替代文件
> 全量加载。定向测试 42 项通过，覆盖自动快照筛选、访问计数和 FTS 行为。

**表结构**（与 `SqliteMemoryProvider` 一致）：
```sql
-- 记忆表
CREATE TABLE IF NOT EXISTS memory_entries (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    name TEXT NOT NULL,
    description TEXT NOT NULL,
    content TEXT NOT NULL,
    type TEXT NOT NULL,          -- 'user' | 'feedback' | 'project' | 'reference'
    scope TEXT NOT NULL,         -- 'private' | 'team'
    activation TEXT NOT NULL DEFAULT 'OnDemand',  -- 'OnDemand' | 'Core'
    tags_json TEXT NOT NULL,     -- JSON 数组
    source TEXT NOT NULL,
    project TEXT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    last_accessed_at TEXT NULL,
    access_count INTEGER NOT NULL DEFAULT 0
);

-- 用户画像表（单 JSON 列，而非扁平列）
CREATE TABLE IF NOT EXISTS user_profiles (
    user_id TEXT PRIMARY KEY,
    profile_json TEXT NOT NULL,  -- JSON: { display_name, preferences, projects, stack, style, last_updated }
    updated_at TEXT NOT NULL
);

-- 索引
CREATE INDEX IF NOT EXISTS idx_memory_private ON memory_entries (user_id, scope, updated_at DESC);
CREATE INDEX IF NOT EXISTS idx_memory_team ON memory_entries (project, scope, updated_at DESC);

-- FTS5 虚拟表（trigram tokenizer，支持中文子串匹配）
CREATE VIRTUAL TABLE IF NOT EXISTS memory_fts USING fts5(
    memory_id UNINDEXED,
    name,
    description,
    content,
    tags,
    tokenize = 'trigram'
);
```

**召回与排序**：
- **召回**：FTS5 trigram 匹配，`SearchRelevantMemoriesAsync` 内部先取 4 倍于 `maxResults` 的候选（`MaxResults * 4`）再排序。
- **排序**：以 FTS 词法相关性为主，访问频率最多 10% 修正、近期访问最多 5% 修正（`GetMemoryRank`）。
- **访问计数**：`TouchMemoryAsync` 在记忆进入 `ActiveEntries` 或 `search_memory` 返回结果时各记一次；Core 常驻注入不计数。

**优点**：无外部服务依赖、本地快速全文检索、中文支持好。
**缺点**：不支持向量语义检索（当前由词法 FTS 承担）。

### 6.3 PostgreSQL Memory Provider（设计目标，尚未实现）

> **当前实现状态（2026-08）**：仍为早期设计目标，`PostgresMemoryProvider` 未实现。

**数据库 Schema**：
```sql
-- 记忆表
CREATE TABLE memories (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id VARCHAR(100) NOT NULL,
    content TEXT NOT NULL,
    type VARCHAR(20) NOT NULL,  -- 'user', 'feedback', 'project', 'reference'
    tags TEXT[] DEFAULT '{}',
    source VARCHAR(50),
    metadata JSONB DEFAULT '{}',
    embedding vector(1536),  -- OpenAI ada-002 维度
    created_at TIMESTAMP DEFAULT NOW(),
    last_accessed_at TIMESTAMP,
    access_count INTEGER DEFAULT 0
);

-- 用户画像表
CREATE TABLE user_profiles (
    user_id VARCHAR(100) PRIMARY KEY,
    display_name VARCHAR(200),
    preferences JSONB DEFAULT '{}',
    projects JSONB DEFAULT '[]',
    stack JSONB DEFAULT '{}',
    style JSONB DEFAULT '{}',
    last_updated TIMESTAMP DEFAULT NOW()
);

-- 索引
CREATE INDEX idx_memories_user_id ON memories(user_id);
CREATE INDEX idx_memories_type ON memories(type);
CREATE INDEX idx_memories_tags ON memories USING GIN(tags);
CREATE INDEX idx_memories_embedding ON memories USING ivfflat (embedding vector_cosine_ops);
```

**优点**：高性能语义搜索、支持复杂查询、可扩展性强。
**缺点**：需要外部依赖、需要嵌入 API 调用（有成本）。

## 7. Tool Definitions

> **当前实现状态（2026-08）**：工具已从 3 个扩展到 5 个（新增 `update_memory` / `delete_memory`）。
> 记忆类型从 `fact/procedure/context/reference` 更新为 `user/feedback/project/reference`。

### 7.1 Save Memory Tool

```csharp
[Tool("save_memory", "保存信息到长期记忆中，支持自动分类和标签")]
public class SaveMemoryTool : IToolExecutor
{
    [ToolParameter("content", "要保存的内容", required: true)]
    public string Content { get; set; }

    [ToolParameter("type", "记忆类型：user（用户）、feedback（反馈）、project（项目）、reference（参考）", required: false)]
    public string? Type { get; set; }

    [ToolParameter("tags", "标签列表，用逗号分隔", required: false)]
    public string? Tags { get; set; }

    [ToolParameter("project", "关联的项目名称", required: false)]
    public string? Project { get; set; }

    [ToolParameter("activation", "注入策略：on_demand（按任务检索）或 core（每轮常驻）", required: false)]
    public string? Activation { get; set; }
}
```

### 7.2 Search Memory Tool

```csharp
[Tool("search_memory", "搜索长期记忆，使用自然语言查询")]
public class SearchMemoryTool : IToolExecutor
{
    [ToolParameter("query", "搜索查询", required: true)]
    public string Query { get; set; }

    [ToolParameter("type", "限定搜索的记忆类型", required: false)]
    public string? Type { get; set; }

    [ToolParameter("project", "限定搜索的项目", required: false)]
    public string? Project { get; set; }

    [ToolParameter("max_results", "最大返回结果数", required: false)]
    public int? MaxResults { get; set; }
}
```

### 7.3 Update Memory Tool

```csharp
[Tool("update_memory", "更新现有的长期记忆")]
public class UpdateMemoryTool : IToolExecutor
{
    [ToolParameter("memory_id", "要更新的记忆 ID", required: true)]
    public string MemoryId { get; set; }

    [ToolParameter("content", "新的记忆内容", required: false)]
    public string? Content { get; set; }

    [ToolParameter("type", "新的记忆类型", required: false)]
    public string? Type { get; set; }

    [ToolParameter("tags", "新的标签列表", required: false)]
    public string? Tags { get; set; }

    [ToolParameter("activation", "新的注入策略：on_demand 或 core", required: false)]
    public string? Activation { get; set; }
}
```

### 7.4 Delete Memory Tool

```csharp
[Tool("delete_memory", "删除一条长期记忆")]
public class DeleteMemoryTool : IToolExecutor
{
    [ToolParameter("memory_id", "要删除的记忆 ID", required: true)]
    public string MemoryId { get; set; }
}
```

### 7.5 Get User Profile Tool

```csharp
[Tool("get_user_profile", "获取用户偏好和项目上下文")]
public class GetUserProfileTool : IToolExecutor
{
    [ToolParameter("project", "当前项目名称（可选，用于获取项目特定上下文）", required: false)]
    public string? Project { get; set; }
}
```

## 8. Integration Points

### 8.1 System Prompt Injection

> **当前实现状态（2026-08-04）**：`RunStreamAsync()` 在用户 Turn 开始时创建一次
> `ActiveMemorySnapshot`（Core 常驻 + Active 按当前输入召回），同一 Turn 的所有 LLM Round
> 复用该不可变快照；由 `ActiveMemorySnapshot.FormatAsString()` 生成 Prompt 文本，Agent 不承担
> 展示逻辑。动态 System Prompt 每轮由 `SystemPromptBuilder` 重建，记忆作为 Layer 4 注入。

`RunStreamAsync()` 负责在 Turn 开始时创建快照；系统提示词构建器只消费已格式化的快照文本，不能自行检索或创建快照：

```csharp
// Agent.RunStreamAsync：每个用户 Turn 仅创建一次。
var snapshot = await _memoryManager.CreateActiveMemorySnapshotAsync(
    _config.UserId,
    input,
    sessionId,
    cancellationToken: cancellationToken);

// Agent.BuildSystemPromptAsync：每个 LLM Round 重建 Prompt，复用同一快照。
var systemPrompt = await SystemPromptBuilder.BuildAsync(new SystemPromptParams
{
    // Layer 1–3 和其他 Layer 4 动态信息省略
    MemoryIndex = snapshot.FormatAsString()
});
```

### 8.2 Future Context Compression Integration

当前实现的上下文压缩消费 `SessionMemoryHook` 生成的 `MEMORY.md`；下列 `MemoryAwareCompactStrategy` 是未来将长期记忆写入压缩链路的设计草案，尚未实现。

```csharp
public class MemoryAwareCompactStrategy : ICompactStrategy
{
    public async Task<List<Message>> CompactAsync(List<Message> messages, CompactContext context)
    {
        // 1. 提取对话中的重要信息
        var importantInfo = ExtractImportantInformation(messages);
        
        // 2. 保存到记忆系统
        foreach (var info in importantInfo)
        {
            await _memoryManager.SaveMemoryAsync(
                context.UserId,
                info.Content,
                info.Type,
                info.Tags,
                source: "conversation_extraction");
        }
        
        // 3. 执行标准压缩
        return await _innerStrategy.CompactAsync(messages, context);
    }
}
```

### 8.3 Future Skill System Integration

当前没有基于记忆自动建议或激活 Skill 的实现；下列代码为设计草案。

```csharp
public class MemorySkillIntegration
{
    public async Task<List<ISkill>> SuggestSkillsAsync(string userId, string context)
    {
        // 1. 搜索相关程序记忆
        var procedures = await _memoryManager.SearchRelevantMemoriesAsync(
            userId, context, type: MemoryType.Reference);
        
        // 2. 匹配已注册的技能
        var suggestedSkills = new List<ISkill>();
        foreach (var procedure in procedures)
        {
            var matchingSkill = _skillRegistry.FindByPattern(procedure.Content);
            if (matchingSkill != null)
            {
                suggestedSkills.Add(matchingSkill);
            }
        }
        
        return suggestedSkills;
    }
}
```

## 9. Historical / Future Implementation Plan

> 本节保留早期实施路线作为背景，不描述当前完成状态；实际状态以第 16 节为准。

### Phase 1: Core Memory System (2-3 days)

1. **Create interfaces and models**
   - `IMemoryProvider`
   - `IMemoryManager`
   - `MemoryEntry`, `UserProfile` models

2. **Implement FileMemoryProvider**
   - File-based storage
   - Basic CRUD operations
   - Simple keyword search

3. **Implement Memory Tools**
   - `SaveMemoryTool`
   - `SearchMemoryTool`
   - `GetUserProfileTool`

4. **Update StaticToolExamples**
   - Replace test stubs with real implementation

### Phase 2: PostgreSQL Provider (2-3 days)

1. **Database Schema**
   - Create migration scripts
   - Set up vector extension

2. **Implement PostgresMemoryProvider**
   - CRUD operations
   - Vector embedding generation
   - Semantic search

3. **Integration with existing PostgreSQL setup**
   - Leverage existing `InsightaAI.LLM` infrastructure

### Phase 3: Advanced Features (3-4 days)

1. **Auto-extraction**
   - Extract memories from conversations
   - Update user profile based on interactions

2. **System Prompt Integration**
   - Inject memories into system prompt
   - Dynamic context building

3. **Context Compression Integration**
   - Memory-aware compression
   - Preserve important information

4. **Skill Integration**
   - Suggest skills based on memories
   - Convert procedures to skills

## 10. Future Configuration Design

> **当前实现状态（2026-08-04）**：以下 JSON 尚未接入运行时配置绑定。当前 CLI
> 由 `AgentFactory` 直接创建 `SqliteMemoryProvider`，默认数据库路径为
> `~/.insighta/memory/memory.db`。本节保留为将来的可配置化设计，不应视为当前可用配置。

```json
{
  "Memory": {
    "Provider": "sqlite",  // "sqlite"（当前主存储）| "file"（迁移/兼容）
    "SqlitePath": "~/.insighta/memory/memory.db",
    "FileStoragePath": "~/.insighta/memories",
    "ConnectionString": "${CONNECTION_STRING}",
    "EmbeddingModel": "text-embedding-ada-002",
    "MaxMemoriesPerUser": 10000,
    "SearchResultLimit": 10,
    "AutoExtractEnabled": true,
    "AutoExtractThreshold": 0.7  // Confidence threshold for auto-extraction
  }
}
```

## 11. Testing Strategy

### Unit Tests
- `MemoryEntry` serialization/deserialization
- `FileMemoryProvider` CRUD operations
- `MemoryManager` search logic
- Tag extraction and classification

### Integration Tests
- `PostgresMemoryProvider` with real database
- Vector search accuracy
- System prompt injection
- Context compression with memory preservation

### Performance Tests
- Search latency benchmarks
- Concurrent access tests
- Large memory set tests (10k+ entries)

## 12. Security Considerations

1. **Retrieval isolation (implemented)**: SQLite search/list operations filter private memories by `user_id`; team memories are included only when the caller supplies a matching `projectId`.
2. **Mutation authorization (known limitation; deferred)**: `UpdateMemoryAsync` and `DeleteMemoryAsync` load by memory ID only and do not verify the caller's `userId`. This is intentionally recorded but not fixed yet: the follow-up must first define Team memory project-membership authorization, then verify `existing.UserId == userId` for Private memories and membership for Team memories. A caller-supplied project string is not authorization evidence.
3. **Encryption (future)**: Sensitive memories may be encrypted at rest; no encryption layer is implemented today.
4. **Retention and GDPR (future)**: Retention policies and dedicated deletion/export workflows are not implemented.

## 13. Session Memory (Short-Term Memory)

### 13.1 概述

会话记忆是**会话级短期记忆**，用于本地持久化会话摘要并辅助上下文压缩。它的价值在于：

- **上下文压缩**：作为 L2 压缩策略的摘要来源；压缩阶段不再调用 LLM，但摘要可由 `ISummaryService` 在后台预先更新。
- **会话连续性**：在长对话中保持关键信息。

Tool Error Memory 属于第 14 节的后续能力，当前尚未实现。

### 13.2 存储结构

```
~/.insighta/sessions/{sessionId}/memories/
├── MEMORY.md                # 会话级记忆摘要
└── metadata.json            # 会话元数据
```

### 13.3 SessionMemoryHook

实现 `IAgentEventHook` 接口。Round 结束（达到配置的最小轮次）或 Turn 结束时，它会保存消息快照并在后台提取记忆：

```csharp
public sealed class SessionMemoryHook : IAgentEventHook
{
    public Task OnAgentRoundEndedAsync(
        AgentEventHookContext context,
        IReadOnlyList<Message> messages,
        Message? assistantMessage,
        CancellationToken cancellationToken = default)
    {
        ExtractMemoryInBackground(context, messages);
        return Task.CompletedTask;
    }
}
```

**提取策略**：

- `SessionMemoryHook` 将已有摘要与最近最多 10 条消息交给 `ISummaryService.UpdateAsync()`；成功后原子地替换 `MEMORY.md`。
- LLM 摘要可通过 `SessionMemoryOptions.EnableLlmSummary` 关闭；默认开启，并受最小轮次和摘要间隔控制。
- 摘要失败时不覆盖已有 `MEMORY.md`。

### 13.4 与 L2 压缩的集成

会话记忆是 L2 Session Memory Compact 的数据来源：

```
对话轮次 → SessionMemoryHook 异步提取 → MEMORY.md
占用率分母：AvailableInputTokens = MaxContextTokens - ReservedForOutput
当上下文达到 45% → MicroCompact (L1)
当上下文达到 65% → SessionMemoryCompact (L2)
  └── 读取 MEMORY.md 作为摘要
  └── 替换旧消息，保留最近 N 轮
当上下文达到 80% → TraditionalCompact (L3)
  └── 调用 LLM 生成摘要
```

## 14. Tool Error Memory (协同进化)

### 14.1 核心思想

工具调用的错误和修复过程是**高价值的学习数据**：

- **错误模式**：避免重复犯同样的错误
- **修复模式**：学习如何从错误中恢复
- **成功模式**：记录最佳实践

这实现了一种"协同进化"——Agent 通过与用户的交互不断学习和改进。

### 14.2 记录结构

```markdown
---
tool: bash
error_type: FileNotFoundError
timestamp: 2025-01-15T10:30:00Z
resolved: true
---

## 失败调用
```json
{"command": "cat /tmp/nonexistent.txt"}
```

## 错误信息
No such file or directory: /tmp/nonexistent.txt

## 修复调用（如果有）
```json
{"command": "cat /tmp/correct.txt"}
```

## 修复说明
路径拼写错误，用户纠正后使用正确路径

## 关键词
bash, FileNotFoundError, 路径, 不存在
```

### 14.3 修复知识的来源

"如何知道怎么修复"是核心问题。修复知识来自三个渠道：

**1. 对比失败与成功**

最有价值的场景：Agent 调用失败 → 用户纠正或自我修正 → 调用成功

```
Round 1: bash("ls /nonexistent") → 错误
Round 2: bash("ls /correct/path") → 成功
→ 记录：路径类错误的修复模式
```

**2. 解析错误信息**

许多工具错误包含足够信息推断修复方式：

| 错误类型 | 修复推断 |
|---------|---------|
| FileNotFoundError | 检查路径拼写，使用绝对路径 |
| ParameterRequired | 添加缺失的必填参数 |
| PermissionDenied | 换用有权限的路径 |
| CommandNotFound | 检查命令拼写或安装 |
| TypeMismatch | 检查参数类型 |

**3. 用户显式反馈**

用户说 "你应该用 xxx" 或 "下次记得先 xxx"，这是最直接的修复知识。

### 14.4 使用方式

**方式 A：注入 System Prompt**

在工具描述中追加历史错误提示：

```
## bash
执行 Shell 命令。

### 历史错误提示
- 注意：上次因路径 "/tmp/nonexistent.txt" 不存在导致失败，请确认路径存在
```

**方式 B：调用前检查**

在工具执行前，检查该工具的历史错误记录：

```csharp
public class ToolErrorAwareHook : IToolHook
{
    public async Task<ToolHookResult> OnBeforeExecutionAsync(
        string toolName, string arguments, ToolExecutionContext context)
    {
        var errors = await _memoryManager.SearchToolErrorsAsync(toolName, arguments);
        if (errors.Any())
        {
            // 注入警告到上下文
            context.AddWarning($"历史错误提示：{errors.First().Description}");
        }
        return ToolHookResult.Allow;
    }
}
```

### 14.5 错误分类与标签

自动为错误打标签以便检索：

```csharp
public static class ToolErrorClassifier
{
    public static List<string> Classify(string errorMessage)
    {
        var tags = new List<string>();

        if (errorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase))
            tags.Add("path-issue");
        if (errorMessage.Contains("permission", StringComparison.OrdinalIgnoreCase))
            tags.Add("permission-issue");
        if (errorMessage.Contains("required", StringComparison.OrdinalIgnoreCase))
            tags.Add("missing-parameter");
        if (errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            tags.Add("timeout");

        return tags;
    }
}
```

## 15. Memory Deduplication

### 15.1 问题

相同或相似的记忆可能被重复保存：

```
- 用户的真实姓名是秦元培
- 用户的真实姓名是秦元培  (重复)
- 用户的名字叫秦元培       (语义相同)
```

### 15.2 解决方案

在保存前检查相似记忆：

```csharp
public async Task<MemoryEntry> SaveMemoryAsync(...)
{
    // 查找相似记忆
    var existing = await FindSimilarMemoryAsync(userId, content, type, project, cancellationToken);

    if (existing != null)
    {
        // 更新现有记忆而非创建新记忆
        existing.Content = content;
        existing.UpdatedAt = DateTime.UtcNow;
        await _provider.UpdateMemoryAsync(existing, cancellationToken);
        return existing;
    }

    // 创建新记忆
    // ...
}
```

### 15.3 相似度计算

两层匹配策略：

1. **实体匹配**（70% 阈值）：提取人名、项目名等实体，比较重叠度
2. **关键词匹配**（80% 阈值）：提取关键词，计算 Jaccard 相似度

```csharp
private static bool IsContentSimilar(string content1, string content2)
{
    // 1. 实体匹配
    var entities1 = ExtractEntities(content1);
    var entities2 = ExtractEntities(content2);
    if (entities1.Count > 0 && entities2.Count > 0)
    {
        var overlap = entities1.Intersect(entities2, StringComparer.OrdinalIgnoreCase).Count();
        var similarity = (float)overlap / Math.Max(entities1.Count, entities2.Count);
        if (similarity >= 0.7f) return true;
    }

    // 2. 关键词匹配
    var keywords1 = ExtractKeywords(content1);
    var keywords2 = ExtractKeywords(content2);
    var keywordOverlap = keywords1.Intersect(keywords2, StringComparer.OrdinalIgnoreCase).Count();
    var keywordSimilarity = (float)keywordOverlap / Math.Min(keywords1.Count, keywords2.Count);
    return keywordSimilarity >= 0.8f;
}
```

## 16. Implementation Status

### 已完成

- [x] `IMemoryProvider` + `SqliteMemoryProvider`（SQLite + FTS5 trigram，运行时主存储）
- [x] `IMemoryProvider` + `FileMemoryProvider`（迁移与兼容保留）
- [x] `MemoryManager` + `IMemoryManager`
- [x] `MemoryEntry`, `UserProfile` 模型（含 Type/Scope/Activation）
- [x] `ActiveMemorySnapshot` 每 Turn 不可变快照（Core 常驻 + Active 召回）
- [x] `save_memory`, `search_memory`, `update_memory`, `delete_memory`, `get_user_profile` 工具
- [x] System Prompt 注入 Provider 记忆索引 + 活跃记忆快照格式化
- [x] 记忆去重逻辑
- [x] `IAgentEventHook` 接口
- [x] `SessionMemoryHook` 会话记忆
- [x] Agent 钩子触发机制
- [x] L2 `SessionMemoryCompactStrategy`
- [x] 访问计数（粗粒度）与排序修正（频率 ≤10%、近期 ≤5%）
- [x] 自动注入门槛（≥3 trigram、候选 ≥2 命中且覆盖 50% 查询片段）
- [x] 定向测试 42 项通过（2026-08-04）

### 待实现

- [ ] Tool Error Memory 记录和使用
- [ ] PostgreSQL Memory Provider（早期设计目标）
- [ ] 向量语义搜索支持
- [ ] 用户画像自动更新
- [ ] Memory 自动注入门槛校准（基于本地筛选日志调参）

---

**Document Version**: 2.1
**Last Updated**: 2026-08-04
**Author**: InsightaAI Team
