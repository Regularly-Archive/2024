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
2. **Semantic Search**: Find relevant memories using natural language queries
3. **Memory Types**: Distinguish between facts, procedures, and user preferences
4. **Storage Flexibility**: Support file-based and database storage
5. **Integration**: Seamlessly integrate with existing context compression and skill systems
6. **Performance**: Fast retrieval with minimal latency

## 3. Architecture Overview

```
Memory System
├── IMemoryProvider                    # Storage abstraction
│   ├── FileMemoryProvider             # Markdown file storage
│   └── PostgresMemoryProvider         # Database storage with vector search
├── MemoryManager                      # Core memory operations
│   ├── SaveMemoryAsync()              # Store new memory
│   ├── SearchMemoriesAsync()          # Semantic search
│   └── GetUserProfileAsync()          # Get user preferences
├── Memory Tools
│   ├── SaveMemoryTool                 # Tool: save memory
│   ├── SearchMemoryTool               # Tool: search memories
│   └── GetUserProfileTool             # Tool: get user context
└── Memory Injection                   # Inject into system prompt
    └── SystemPromptBuilder            # Build enhanced system prompt
```

## 4. Data Models

### 4.1 Memory Entry

```csharp
public class MemoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; }
    public string Content { get; set; }
    public MemoryType Type { get; set; }
    public List<string> Tags { get; set; } = new();
    public string Source { get; set; }  // "user_input", "agent_inference", "file_import"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAccessedAt { get; set; }
    public int AccessCount { get; set; }
    public float? RelevanceScore { get; set; }  // For search results
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public enum MemoryType
{
    Fact,        // 事实：用户偏好、项目信息、环境配置
    Procedure,   // 流程：可复用的操作步骤、工作流
    Context,     // 上下文：当前项目状态、决策历史
    Reference    // 参考：代码片段、配置示例
}
```

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

```csharp
public interface IMemoryProvider
{
    /// <summary>
    /// 保存记忆
    /// </summary>
    Task SaveMemoryAsync(MemoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取记忆
    /// </summary>
    Task<MemoryEntry?> GetMemoryAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 语义搜索记忆
    /// </summary>
    Task<List<MemoryEntry>> SearchMemoriesAsync(
        string userId, 
        string query, 
        MemoryType? type = null,
        int maxResults = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户的所有记忆（分页）
    /// </summary>
    Task<List<MemoryEntry>> ListMemoriesAsync(
        string userId,
        MemoryType? type = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新记忆
    /// </summary>
    Task UpdateMemoryAsync(MemoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除记忆
    /// </summary>
    Task DeleteMemoryAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户画像
    /// </summary>
    Task<UserProfile?> GetUserProfileAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存用户画像
    /// </summary>
    Task SaveUserProfileAsync(UserProfile profile, CancellationToken cancellationToken = default);
}
```

### 5.2 Memory Manager

```csharp
public interface IMemoryManager
{
    /// <summary>
    /// 保存记忆（自动分类和打标签）
    /// </summary>
    Task<MemoryEntry> SaveMemoryAsync(
        string userId,
        string content,
        MemoryType? type = null,
        List<string>? tags = null,
        string? source = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 智能搜索（结合语义搜索和关键词匹配）
    /// </summary>
    Task<List<MemoryEntry>> SearchRelevantMemoriesAsync(
        string userId,
        string context,
        int maxResults = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户上下文（合并画像和相关记忆）
    /// </summary>
    Task<string> GetUserContextAsync(
        string userId,
        string? currentProject = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新用户画像（基于对话历史）
    /// </summary>
    Task UpdateUserProfileAsync(
        string userId,
        Dictionary<string, string> updates,
        CancellationToken cancellationToken = default);
}
```

## 6. Storage Implementations

### 6.1 File Memory Provider

存储位置：`~/.insightai/memory/{userId}/`

```
~/.insightai/memory/{userId}/
├── profile.md           # 用户画像
├── memories/
│   ├── {id}.md          # 单条记忆
│   └── index.json       # 索引文件
└── projects/
    └── {project}.md     # 项目特定记忆
```

**记忆文件格式**：
```markdown
---
id: mem_abc123
type: fact
tags: [preference, coding-style]
source: user_input
created: 2024-01-15T10:30:00Z
---

用户偏好使用 4 空格缩进，不使用 Tab。
喜欢使用 var 关键字进行类型推断。
```

**优点**：
- 人类可读，易于手动编辑
- 版本控制友好
- 无外部依赖

**缺点**：
- 搜索性能较差（需全量扫描）
- 不支持向量搜索

### 6.2 PostgreSQL Memory Provider

**数据库 Schema**：
```sql
-- 记忆表
CREATE TABLE memories (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id VARCHAR(100) NOT NULL,
    content TEXT NOT NULL,
    type VARCHAR(20) NOT NULL,  -- 'fact', 'procedure', 'context', 'reference'
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

**优点**：
- 高性能语义搜索
- 支持复杂查询
- 可扩展性强

**缺点**：
- 需要外部依赖
- 需要嵌入 API 调用（有成本）

## 7. Tool Definitions

### 7.1 Save Memory Tool

```csharp
[Tool("save_memory", "保存信息到长期记忆中，支持自动分类和标签")]
public class SaveMemoryTool : IToolExecutor
{
    [ToolParameter("content", "要保存的内容", required: true)]
    public string Content { get; set; }
    
    [ToolParameter("type", "记忆类型：fact（事实）、procedure（流程）、context（上下文）、reference（参考）", required: false)]
    public string? Type { get; set; }
    
    [ToolParameter("tags", "标签列表，用逗号分隔", required: false)]
    public string? Tags { get; set; }
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
    
    [ToolParameter("max_results", "最大返回结果数", required: false)]
    public int? MaxResults { get; set; }
}
```

### 7.3 Get User Profile Tool

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

在构建系统提示词时，自动注入相关记忆：

```csharp
public class SystemPromptBuilder
{
    public async Task<string> BuildAsync(AgentContext context)
    {
        var sb = new StringBuilder();
        
        // 1. 基础系统提示
        sb.AppendLine(context.BaseSystemPrompt);
        
        // 2. 用户画像
        var userProfile = await _memoryManager.GetUserProfileAsync(context.UserId);
        if (userProfile != null)
        {
            sb.AppendLine("\n## User Preferences");
            sb.AppendLine($"- Language: {userProfile.Style.Language}");
            sb.AppendLine($"- Verbosity: {userProfile.Style.Verbosity}");
            // ... 其他偏好
        }
        
        // 3. 相关记忆（基于当前对话上下文）
        var relevantMemories = await _memoryManager.SearchRelevantMemoriesAsync(
            context.UserId, 
            context.CurrentMessage,
            maxResults: 3);
        
        if (relevantMemories.Any())
        {
            sb.AppendLine("\n## Relevant Memories");
            foreach (var memory in relevantMemories)
            {
                sb.AppendLine($"- [{memory.Type}] {memory.Content}");
            }
        }
        
        return sb.ToString();
    }
}
```

### 8.2 Context Compression Integration

在上下文压缩时，保留重要记忆：

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

### 8.3 Skill System Integration

记忆可以触发技能激活：

```csharp
public class MemorySkillIntegration
{
    public async Task<List<ISkill>> SuggestSkillsAsync(string userId, string context)
    {
        // 1. 搜索相关程序记忆
        var procedures = await _memoryManager.SearchRelevantMemoriesAsync(
            userId, context, type: MemoryType.Procedure);
        
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

## 9. Implementation Plan

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

## 10. Configuration

```json
{
  "Memory": {
    "Provider": "postgres",  // "file" or "postgres"
    "FileStoragePath": "~/.insightai/memory",
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

1. **Data Isolation**: User memories are isolated by `user_id`
2. **Encryption**: Sensitive memories can be encrypted at rest
3. **Access Control**: Memories are only accessible by the owning user
4. **Data Retention**: Configurable retention policies
5. **GDPR Compliance**: Support for data deletion requests

## 13. Session Memory (Short-Term Memory)

### 13.1 概述

会话记忆是**短期记忆**，仅在单次会话期间有效。它的价值在于：

- **上下文压缩**：作为 L2 压缩策略的摘要来源（零成本）
- **工具协同**：记录工具调用的错误和成功模式，实现"协同进化"
- **会话连续性**：在长对话中保持关键信息

### 13.2 存储结构

```
~/.insightai/memory/sessions/{sessionId}/
├── session-memory.md        # 会话级记忆摘要
├── metadata.json            # 会话元数据
└── tool-errors/             # 工具调用记录
    ├── {errorId}.md         # 单条错误记录
    └── ...
```

### 13.3 SessionMemoryHook

实现 `IAgentHook` 接口，在每轮结束后异步提取记忆：

```csharp
public sealed class SessionMemoryHook : IAgentHook
{
    // 每轮结束后触发
    public async Task OnRoundEndAsync(int round, IReadOnlyList<Message> messages,
        Message? assistantMessage, CancellationToken cancellationToken)
    {
        // 异步执行，不阻塞主流程
        _ = Task.Run(async () =>
        {
            await ExtractAndSaveMemoryAsync(round, messages, assistantMessage, cancellationToken);
        }, cancellationToken);
    }
}
```

**提取策略**：
- 用户偏好关键词：喜欢、偏好、不要、don't
- 项目信息关键词：项目、project、目标、goal、截止、deadline
- 决策关键词：决定、decide、选择、choose、方案、approach
- 问题关键词：错误、error、问题、issue、bug

### 13.4 与 L2 压缩的集成

会话记忆是 L2 Session Memory Compact 的数据来源：

```
对话轮次 1-10 → SessionMemoryHook 异步提取 → session-memory.md
占用率分母：AvailableInputTokens = MaxContextTokens - ReservedForOutput
当上下文达到 45% → MicroCompact (L1)
当上下文达到 65% → SessionMemoryCompact (L2)
  └── 读取 session-memory.md 作为摘要
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
        var similarity = (float)overlap / Math.Min(entities1.Count, entities2.Count);
        if (similarity >= 0.7f) return true;
    }

    // 2. 关键词匹配
    var keywords1 = ExtractKeywords(content1);
    var keywords2 = ExtractKeywords(content2);
    var keywordOverlap = keywords1.Intersect(keywords2, StringComparer.OrdinalIgnoreCase).Count();
    var keywordSimilarity = (float)keywordOverlap / Math.Max(keywords1.Count, keywords2.Count);
    return keywordSimilarity >= 0.8f;
}
```

## 16. Implementation Status

### 已完成

- [x] `IMemoryProvider` + `FileMemoryProvider`
- [x] `MemoryManager` + `IMemoryManager`
- [x] `MemoryEntry`, `UserProfile` 模型
- [x] `save_memory`, `search_memory`, `get_user_profile` 工具
- [x] System Prompt 注入记忆索引 (MEMORY.md)
- [x] 记忆去重逻辑
- [x] `IAgentHook` 接口
- [x] `SessionMemoryHook` 会话记忆
- [x] Agent 钩子触发机制

### 待实现

- [ ] Tool Error Memory 记录和使用
- [ ] L2 Session Memory Compact 策略
- [ ] PostgreSQL Memory Provider
- [ ] 向量搜索支持
- [ ] 用户画像自动更新

---

**Document Version**: 2.0
**Last Updated**: 2025-01-15
**Author**: InsightaAI Team
