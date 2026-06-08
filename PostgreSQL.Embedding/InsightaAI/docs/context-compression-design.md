# InsightaAI 上下文压缩设计方案

> 参考 Claude Code 的三级压缩策略，为 InsightaAI Agent 设计的上下文管理方案

---

## 1. 问题背景

随着对话变长，每轮 LLM 调用都发送全部消息历史，导致：

- Token 消耗线性增长，成本失控
- 响应延迟增加
- 超出模型上下文窗口直接报错
- 大量无关历史信息干扰模型判断

---

## 2. 设计目标

1. 防止上下文窗口溢出
2. 最小化压缩过程中的信息损失
3. 最小化延迟开销（优先零成本方案）
4. 支持 prompt cache 复用
5. 压缩后恢复关键附件（文件、Skills、MCP 指令）

---

## 3. 核心约束：Tool Use/Result 配对

> **API 硬性要求**：`tool_use` 和 `tool_result` 消息必须成对出现，不能拆散。

```
正确 ✓:
  [Assistant] tool_use: bash(command="ls")
  [ToolResult] tool_result: "file1.txt\nfile2.txt"

错误 ✗:
  [Assistant] tool_use: bash(command="ls")
  [Summary] "之前的对话摘要..."  // tool_result 丢失！
```

**压缩策略必须遵守**：
1. 压缩时，`tool_use` 和 `tool_result` 作为原子单元处理
2. 如果压缩某个 `tool_result`，其对应的 `tool_use` 也必须一起压缩或保留
3. 微压缩只清理 `tool_result` 内容，保留配对结构
4. 传统压缩时，整对一起移除或保留

---

## 4. 三级压缩架构

```
+--------------------------------------------------------------------+
|                    ContextManager                                    |
|                                                                      |
|  +--------------------------------------------------------------+  |
|  |  ITokenEstimator                                              |  |
|  |  +-- CharTokenEstimator (char-level estimation, no deps)     |  |
|  +--------------------------------------------------------------+  |
|                                                                      |
|  +--------------------------------------------------------------+  |
|  |  ICompactStrategy[] (executed by priority)                    |  |
|  |  +-- Level 1: MicroCompactStrategy (zero cost)               |  |
|  |  +-- Level 3: TraditionalCompactStrategy (LLM summary)       |  |
|  +--------------------------------------------------------------+  |
|                                                                      |
|  +--------------------------------------------------------------+  |
|  |  AttachmentManager (track and restore attachments)            |  |
|  |  +-- Recent file contents (max 5 files, 50K token budget)    |  |
|  |  +-- Active Skills content                                   |  |
|  |  +-- MCP Instructions                                        |  |
|  +--------------------------------------------------------------+  |
|                                                                      |
|  +--------------------------------------------------------------+  |
|  |  UsageTracker (token usage tracking)                          |  |
|  |  +-- Cumulative token statistics                             |  |
|  |  +-- Threshold detection and auto-trigger                    |  |
|  +--------------------------------------------------------------+  |
|                                                                      |
+--------------------------------------------------------------------+
```

---

## 4. Level 1: MicroCompact

**Trigger**: Token usage reaches 60% of available context
**Cost**: Zero (no LLM call)
**Purpose**: Clean up old tool results, keep tool call structure

### Strategy Logic

```
For each tool_use + tool_result pair (in order):
  if pair is old AND not in recent N results:
    if tool is compactable:
      tool_use: keep (preserve call info)
      tool_result: truncate to metadata + tool-specific summary
    else:
      tool_use + tool_result: keep as-is (non-compactable)
  else:
    tool_use + tool_result: keep as-is (recent)
```

### Tool-Specific Truncation Strategy

| Tool | 保留内容 | 截断策略 |
|------|----------|----------|
| bash / powershell | 退出码、最后 5 行输出 | `[command output truncated]\n{last 5 lines}` |
| read_file | 文件路径、总行数 | `[file content truncated: {path}, {totalLines} lines]` |
| grep | 匹配数量、搜索模式 | `[grep results truncated: {count} matches for "{pattern}"]` |
| glob | 文件数量 | `[glob results truncated: {count} files found]` |
| web_search | 搜索结果数量 | `[search results truncated: {count} results for "{query}"]` |
| web_fetch | URL、内容长度 | `[web content truncated: {url}, {length} chars]` |
| edit_file | 文件路径、操作状态 | `[edit completed: {path}]` |
| write_file | 文件路径、写入大小 | `[file written: {path}, {size} bytes]` |

### Metadata Preservation Format

截断后的内容格式：
```
[Tool: {tool_name}] {status}
{tool-specific summary}
{truncated content or status message}
```

示例：
```
[Tool: bash] Success (exit code 0)
Command: git log --oneline -20
[output truncated]
a1b2c3d feat: new feature
e4f5g6h fix: bug fix
...
```

### Compactable Tools

| Tool | Why Compactable |
|------|----------------|
| bash / powershell | Command output is often very long |
| read_file | File content can be re-read |
| grep / glob | Search results can be re-executed |
| web_search / web_fetch | Web content can be re-fetched |
| edit_file / write_file | Edit results are short, but old ones are not important |
| MCP chart tools | Chart data can be regenerated |

### Non-compactable Tools

| Tool | Reason |
|------|--------|
| ask_user | User input is critical context, cannot lose |
| whereami | Environment info is small and critical |

---

## 5. Level 2: Session Memory Compact (Future Enhancement)

**Trigger**: Background memory extraction has been performed
**Cost**: Zero (uses pre-extracted summaries)
**Status**: Deferred to Phase 6

### Design Concept

- Periodically extract key conversation info in background (decisions, file changes, task progress)
- Use pre-extracted memory as summary during compression
- Fastest compression, but requires additional background process

---

## 6. Level 3: Traditional Compact

**Trigger**: Token usage reaches 75% of available context, or manual `/compact` command
**Cost**: One LLM call (can use cheaper model)
**Purpose**: Generate complete conversation summary

### Trigger Modes

| Mode | 说明 |
|------|------|
| Auto | Token 使用量达到阈值时自动触发 |
| Manual | 用户输入 `/compact` 命令手动触发 |
| Reactive | API 返回 `prompt_too_long` 错误时触发 |

### Execution Flow

```
Step 1: Separate messages
+----------------+------------------------+------------------------+
| System msgs    | Old msgs (to compress) | Recent N rounds        |
+----------------+------------------------+------------------------+

Step 2: Strip images and preserve tool pairs
  Old messages: remove images, replace with [image] marker
  Tool pairs: keep structure, truncate results to metadata
  (Reduce token cost for summarization)

Step 3: LLM summary generation
  Prompt (see below)

Step 4: Build compacted message list
+--------------------------------------------------------------+
| 1. Original System messages                                   |
| 2. Compaction boundary marker (structured JSON)               |
| 3. Restored attachments (with fallback handling)              |
| 4. Recent N rounds (kept verbatim)                            |
+--------------------------------------------------------------+
```

### Summary Prompt

```
You are a conversation summarizer for an AI coding assistant.

Summarize the following conversation, focusing on:
1. **User Intent**: What was the user trying to accomplish?
2. **Key Decisions**: Technical decisions made during the conversation
3. **File Changes**: Files created, modified, or deleted (with paths)
4. **Errors & Solutions**: Problems encountered and how they were resolved
5. **Current State**: What task is in progress, what remains to be done

Format your response as structured sections:

## User Intent
[What the user wants to achieve]

## Key Decisions
- [Decision 1]
- [Decision 2]

## File Changes
- Created: [file paths]
- Modified: [file paths]
- Deleted: [file paths]

## Errors & Solutions
- [Error]: [Solution]

## Current State
[What's in progress, what's next]

Keep the summary concise but preserve critical details like file paths, function names, and configuration values.
```

### Structured Boundary Marker

```json
{
  "type": "compaction_boundary",
  "timestamp": "2024-06-08T09:30:00Z",
  "strategy": "TraditionalCompact",
  "pre_compact": {
    "tokens": 156789,
    "messages": 180
  },
  "post_compact": {
    "tokens": 34521,
    "messages": 32
  },
  "restored_attachments": {
    "files": ["readme.md", "config.json"],
    "skills": ["web_search"],
    "mcp_servers": ["sqlite"]
  }
}
```

### Attachment Restoration with Fallback

```
After TraditionalCompact:
  1. Scan recent messages for ReadFileTool calls
  2. Extract file paths from tool arguments
  3. For each file (within budget):
     if file exists AND readable:
       -> Re-read and inject
     else:
       -> Inject marker: "[File no longer available: {path}]"
  4. Ensure Skills content is in system prompt
  5. Ensure MCP instructions are in system prompt
```

### Prompt Cache Support

```
Before compact: [System][Msg1][Msg2]...[Msg100] <- all sent, cache hits old
After compact:  [System][Summary][Msg95]...[Msg100] <- new cache anchor

Key: Cache invalidates before compaction boundary, but subsequent messages can be cached
```

---

## 7. Token Estimator

### CharTokenEstimator (Default)

No external dependencies (no tiktoken), character-level estimation:

| Character Type | Estimated Rate |
|---------------|---------------|
| CJK (Chinese/Japanese/Kapanese) | ~1.5 chars/token |
| English / Latin | ~4 chars/token |
| Message overhead | +4 tokens per message |

```csharp
public int EstimateTokens(string text)
{
    int cjkCount = text.Count(c => c >= 0x4E00 && c <= 0x9FFF);
    int otherCount = text.Length - cjkCount;
    return (int)Math.Ceiling(cjkCount / 1.5 + otherCount / 4.0);
}
```

**Accuracy**: For mixed Chinese-English text, error margin is about +/-15%.

**阈值调整建议**：由于估算误差存在，建议使用保守阈值：

| 阶段 | 配置阈值 | 实际触发点 | 说明 |
|------|----------|-----------|------|
| MicroCompact | 60% | 55% | 预留 5% 缓冲 |
| TraditionalCompact | 75% | 70% | 预留 5% 缓冲 |

> **注**：如果 API 返回 `input_tokens`（如 OpenAI/Anthropic），应优先使用 API 返回的实际值，而非估算值。

---

## 8. Attachment Restoration

After compression, critical context may be lost. AttachmentManager tracks and restores:

### Tracked Attachment Types

| Type | Source | Token Budget |
|------|--------|-------------|
| Recent files | ReadFileTool results | 50,000 tokens, max 5 files |
| Active Skills | Skill activation events | Included in system prompt |
| MCP Instructions | MCP tool activation | Included in system prompt |

### Restoration Logic

```
After TraditionalCompact:
  1. Scan recent messages for ReadFileTool calls
  2. Extract file paths from tool arguments
  3. Re-read files (within budget), inject as messages
  4. Ensure Skills content is in system prompt
  5. Ensure MCP instructions are in system prompt
```

---

## 9. Configuration

### AgentConfig vs ContextBudget

```
AgentConfig.MaxTokens (已有)
  └── 单次 LLM 调用的最大输出 token 数
  └── 例如：4096, 8192, 16384

ContextBudget.MaxContextTokens (新增)
  └── 模型的上下文窗口大小
  └── 例如：128000 (GPT-4o), 200000 (Claude Sonnet)
  └── 用于判断何时触发压缩
```

### ContextBudget Class

```csharp
public sealed record ContextBudget
{
    // Model context window size (e.g., 200000 for Claude Sonnet)
    // Priority: Config > API metadata > hardcoded mapping > default (128K)
    public int MaxContextTokens { get; init; } = 128_000;

    // Level 1 MicroCompact trigger threshold percentage
    public double MicroCompactThreshold { get; init; } = 0.60;

    // Level 3 TraditionalCompact trigger threshold percentage
    public double TraditionalCompactThreshold { get; init; } = 0.75;

    // Tokens reserved for model output
    public int ReservedForOutput { get; init; } = 16_384;

    // MicroCompact: number of recent tool results to keep in full
    public int KeepRecentToolResults { get; init; } = 5;

    // TraditionalCompact: number of recent message rounds to keep
    public int KeepRecentRounds { get; init; } = 10;

    // Max files to restore after compression
    public int MaxFilesToRestore { get; init; } = 5;

    // Token budget for restored files
    public int FileRestoreTokenBudget { get; init; } = 50_000;

    // Summary model (optional, defaults to current model, can use cheaper model)
    public string? SummaryModel { get; init; }
}

// Model context window mapping (fallback)
public static class ModelContextWindows
{
    public static readonly Dictionary<string, int> Defaults = new()
    {
        ["gpt-4o"] = 128_000,
        ["gpt-4o-mini"] = 128_000,
        ["claude-sonnet-4-20250514"] = 200_000,
        ["claude-3-5-sonnet-20241022"] = 200_000,
        ["gemini-2.0-flash"] = 1_048_576,
        // ...
    };
}
```

### Thread Safety

ContextManager 必须是线程安全的，因为：

1. **并行工具执行**：多个工具可能同时更新 AttachmentManager
2. **异步压缩**：压缩过程中可能有新的消息进入

```csharp
public sealed class ContextManager : IContextManager
{
    private readonly SemaphoreSlim _compactLock = new(1, 1);
    private readonly ConcurrentDictionary<string, FileAttachment> _trackedFiles = new();

    public async Task<CompactionResult?> CompactIfNeededAsync(
        List<Message> messages,
        CancellationToken cancellationToken)
    {
        // 确保同一时间只有一个压缩操作
        if (!await _compactLock.WaitAsync(0, cancellationToken))
        {
            return null; // 另一个压缩正在进行
        }

        try
        {
            // ... compression logic
        }
        finally
        {
            _compactLock.Release();
        }
    }
}
```

---

## 10. Integration into Agent Main Loop

### Agent.RunStreamAsync() Modification

```csharp
for (int round = 1; round <= _config.MaxToolRounds; round++)
{
    // NEW: Context compression check
    Message[] requestMessages;
    CompactionResult? compactionResult = null;

    if (_contextManager != null)
    {
        (requestMessages, compactionResult) =
            await _contextManager.CompactIfNeededAsync(
                messages, cancellationToken);

        // Notify UI if compression occurred
        if (compactionResult != null)
        {
            yield return new AgentContextCompactedEvent
            {
                Strategy = compactionResult.StrategyName,
                PreCompactTokens = compactionResult.PreCompactTokens,
                PostCompactTokens = compactionResult.PostCompactTokens,
                PreCompactMessages = compactionResult.PreCompactMessages,
                PostCompactMessages = compactionResult.PostCompactMessages,
                RestoredAttachments = compactionResult.RestoredAttachments
            };
        }
    }
    else
    {
        requestMessages = messages.ToArray();
    }

    // Inject skill instructions...
    // Build LLM request...
}
```

### Manual Trigger: `/compact` Command

```csharp
// In ChatCommand.cs or new CompactCommand.cs
case "/compact":
    if (_contextManager == null)
    {
        renderer.ShowWarning("Context manager not enabled.");
        break;
    }

    renderer.ShowInfo("Compacting context...");

    var result = await _contextManager.ForceCompactAsync(
        messages,
        strategy: args.Length > 0 ? args[0] : "auto",  // auto | micro | traditional
        cancellationToken);

    if (result != null)
    {
        renderer.ShowSuccess(
            $"Compacted ({result.StrategyName}): " +
            $"{result.PreCompactTokens:N0} -> {result.PostCompactTokens:N0} tokens " +
            $"({result.PreCompactMessages} -> {result.PostCompactMessages} messages)");
    }
    else
    {
        renderer.ShowInfo("No compaction needed.");
    }
    break;
```

### ForceCompactAsync Strategy Selection

```csharp
public async Task<CompactionResult?> ForceCompactAsync(
    List<Message> messages,
    string strategy = "auto",
    CancellationToken cancellationToken = default)
{
    return strategy switch
    {
        "micro" => await MicroCompactAsync(messages, cancellationToken),
        "traditional" => await TraditionalCompactAsync(messages, cancellationToken),
        "auto" => await AutoCompactAsync(messages, forceTraditional: true, cancellationToken),
        _ => throw new ArgumentException($"Unknown strategy: {strategy}")
    };
}
```

### New Event Type

```csharp
public sealed record AgentContextCompactedEvent : AgentEvent
{
    public override string EventType => "context_compacted";
    public string Strategy { get; init; } = "";
    public int PreCompactTokens { get; init; }
    public int PostCompactTokens { get; init; }
    public int PreCompactMessages { get; init; }
    public int PostCompactMessages { get; init; }
    public List<string> RestoredAttachments { get; init; } = [];
}
```

### CLI Rendering

```
> /compact
[Context compacted] TraditionalCompact: 156,789 -> 34,521 tokens (180 -> 32 messages)
   Restored: 2 files (readme.md, config.json), Skills instructions

> /compact micro
[Context compacted] MicroCompact: 125,432 -> 89,105 tokens (142 -> 98 messages)
```

---

## 11. Implementation Roadmap

### Phase 1: Core Infrastructure
- [ ] ITokenEstimator + CharTokenEstimator
- [ ] ContextBudget configuration class
- [ ] Model context window mapping
- [ ] ICompactStrategy interface
- [ ] CompactionResult result class
- [ ] ContextManager orchestrator (thread-safe)
- [ ] Integration into Agent.RunStreamAsync()
- [ ] `/compact` manual trigger command

### Phase 2: MicroCompact
- [ ] MicroCompactStrategy implementation
- [ ] Tool-specific truncation strategies
- [ ] Tool type classification (compactable vs non-compactable)
- [ ] Tool Use/Result pair handling

### Phase 3: Traditional Compact
- [ ] TraditionalCompactStrategy implementation
- [ ] LLM summary generation with structured prompt
- [ ] Image stripping before summarization
- [ ] Structured compaction boundary markers
- [ ] Summary model configuration support

### Phase 4: Attachment Restoration
- [ ] AttachmentManager implementation
- [ ] Recent file tracking and restoration (with fallback)
- [ ] Skills content restoration
- [ ] MCP instructions restoration

### Phase 5: Usage Tracking & CLI
- [ ] UsageTracker implementation
- [ ] AgentContextCompactedEvent CLI rendering
- [ ] Real-time token usage display
- [ ] Budget warning notifications

### Phase 6: Future Enhancements
- [ ] Session Memory Compact (Level 2)
- [ ] Background memory extraction process
- [ ] Prompt cache optimization
- [ ] Git checkpoint (auto backup before file edits, support undo)

---

## 12. Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Token estimation | Character-level, no external deps | Fast, zero-latency, sufficiently accurate |
| Token estimation source | API `input_tokens` > estimation | API values are exact, estimation is fallback |
| Threshold buffer | 5% buffer (55%/70% actual trigger) | Compensate for estimation error |
| Summary model | Configurable, defaults to current model | Can use Haiku or cheaper model to reduce cost |
| Compression trigger | Before each LLM call + manual `/compact` | Auto-prevents overflow + user control |
| Summary storage | In-memory (session-level) | Aligned with AgentContext.State lifecycle |
| Compression granularity | Keep recent N message rounds | Simple, predictable, controllable |
| Image handling | Strip before summarization | Reduces token cost of summarization |
| Attachment restoration | Re-read files with fallback | Ensures critical context survives compression |
| Tool pair handling | tool_use + tool_result as atomic unit | API requirement, prevents broken pairs |
| Boundary marker | Structured JSON | Machine-parseable, extensible |
| Thread safety | SemaphoreSlim + ConcurrentDictionary | Safe for parallel tool execution |
| Context window source | Config > API > hardcoded > default | Flexible, reliable |

---

## 13. Reference: Claude Code Behavior

This design is informed by analysis of Claude Code's context management:

1. **MicroCompact**: Zero-cost tool result cleanup
2. **Session Memory Compact**: Background-extracted session summaries
3. **Traditional Compact**: LLM-powered full summarization
4. **Attachment Restoration**: Files, Plans, Skills re-injection
5. **Prompt Cache**: Cache-friendly compaction boundaries
6. **Usage Tracking**: Real-time token display and budget alerts
7. **Manual Trigger**: `/compact` command for user control
