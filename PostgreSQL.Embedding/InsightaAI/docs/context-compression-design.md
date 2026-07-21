# InsightaAI Agent - Context Compression Design Document

> 状态：Historical Design（历史设计）
> 当前实现：压缩阈值、两阶段提交和工具结果生命周期以 [tool-result-lifecycle-design-v2.md](tool-result-lifecycle-design-v2.md) 与 [TODO.md](TODO.md) 为准。本文保留早期方案、命令设想和实施路线，不代表当前 CLI 行为。

## 1. Background & Problem

Currently, `Agent.RunStreamAsync()` sends **all messages** to the LLM on every round:

```csharp
var requestMessages = messages.ToArray();
```

As the conversation grows, this leads to:
- Unbounded token cost growth
- Slower LLM responses
- Context window overflow errors

## 2. Reference: Claude Code's 3-Level Compression

| Level | Strategy | Cost | Speed | Description |
|-------|----------|------|-------|-------------|
| **L1** | MicroCompact | Zero | Instant | Clean old tool results, keep structure, support prompt cache |
| **L2** | Session Memory Compact | Zero | Fast | Use pre-extracted session memory as summary |
| **L3** | Traditional Compact | LLM call | Slow | LLM generates full summary, final fallback |

## 3. Architecture Design

```
ContextManager
├── TokenEstimator              # Char-level token estimation
├── ICompactStrategy            # Strategy interface
│   ├── MicroCompactStrategy    # Level 1: truncate old tool results
│   └── TraditionalCompactStrategy  # Level 3: LLM summary
├── AttachmentManager           # Restore attachments after compaction
└── UsageTracker                # Token usage tracking & threshold detection
```

## 4. Token Estimator

No external dependency (tiktoken). Character-level estimation:

- English: ~4 chars/token
- Chinese: ~1.5 chars/token
- Overhead: +4 tokens per message (role, separators)

```csharp
public class CharTokenEstimator : ITokenEstimator
{
    public int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int cjkCount = text.Count(c => c > 0x4E00 && c < 0x9FFF);
        int otherCount = text.Length - cjkCount;
        return (int)Math.Ceiling(cjkCount / 1.5 + otherCount / 4.0);
    }
}
```

## 5. Context Budget Configuration

```csharp
public sealed record ContextBudget
{
    // Model context window (e.g. 200000 for Claude Sonnet)
    public int MaxContextTokens { get; init; } = 200_000;

    // Level 1 trigger threshold (0.60 = 60%)
    public double MicroCompactThreshold { get; init; } = 0.60;

    // Level 3 trigger threshold (0.75 = 75%)
    public double TraditionalCompactThreshold { get; init; } = 0.75;

    // Reserved tokens for output
    public int ReservedForOutput { get; init; } = 16_384;

    // MicroCompact: keep recent N tool results
    public int KeepRecentToolResults { get; init; } = 5;

    // TraditionalCompact: keep recent N rounds
    public int KeepRecentRounds { get; init; } = 10;

    // Max files to restore after compaction
    public int MaxFilesToRestore { get; init; } = 5;

    // Token budget for restored files
    public int FileRestoreTokenBudget { get; init; } = 50_000;

    // Summary model (optional, defaults to current model)
    public string? SummaryModel { get; init; }
}
```

## 6. Level 1: MicroCompact Strategy

**Zero-cost, instant compression.** Truncates old tool results while keeping structure.

Key behaviors:
- Iterates messages from oldest to newest
- Identifies compactable tool types (bash, read_file, grep, web_search, etc.)
- Truncates tool result text to 200 chars (keep first 100 + last 100)
- Removes non-text content (images) with placeholder
- Preserves recent N tool results untouched
- **Supports prompt cache** (no structural changes, only content truncation)

## 7. Level 3: Traditional Compact Strategy

**LLM-powered summary.** Final fallback when MicroCompact is insufficient.

Key behaviors:
- Separates messages into: system, old (to summarize), recent (to keep)
- **Image stripping**: removes images before summarization, replaces with `[image]`
- Generates concise summary focusing on: key decisions, facts, files modified
- Inserts compression boundary marker: `[Context compacted at YYYY-MM-DD HH:MM:SS]`
- **Attachment restoration**: re-injects recently read files, active Skills, MCP instructions
- Summary model is configurable (can use cheaper model like Haiku)

## 8. Attachment Manager

After compaction, critical context must be restored:

| Attachment Type | Priority | Source |
|----------------|----------|--------|
| Recently read files | High | Track from `read_file` / `write_file` / `edit_file` tool calls |
| Active Skills content | High | From `AgentContext.ActivatedSkills` |
| MCP tool instructions | Medium | From `AgentContext.ActivatedMcpTools` |
| Current plan | High | From `AgentContext.Plan` |

## 9. Integration into Agent Loop

In `Agent.RunStreamAsync()`, before each LLM call:

```csharp
for (int round = 1; round <= _config.MaxToolRounds; round++)
{
    // Context compression check
    Message[] requestMessages;
    if (_contextManager != null)
    {
        var compactResult = await _contextManager.CompactAsync(
            messages, cancellationToken);
        requestMessages = compactResult.CompactedMessages;

        // Notify UI if compaction occurred
        if (compactResult.PreCompactTokens != compactResult.PostCompactTokens)
        {
            yield return new AgentContextCompactedEvent
            {
                StrategyName = compactResult.StrategyName,
                PreCompactTokens = compactResult.PreCompactTokens,
                PostCompactTokens = compactResult.PostCompactTokens,
                PreCompactMessages = compactResult.PreCompactMessages,
                PostCompactMessages = compactResult.PostCompactMessages,
            };
        }
    }
    else
    {
        requestMessages = messages.ToArray();
    }

    // ... rest of agent loop
}
```

## 10. New Event Type

```csharp
public sealed record AgentContextCompactedEvent : AgentEvent
{
    public override string EventType => "context_compacted";
    public string StrategyName { get; init; } = "";
    public int PreCompactTokens { get; init; }
    public int PostCompactTokens { get; init; }
    public int PreCompactMessages { get; init; }
    public int PostCompactMessages { get; init; }
    public List<string> RestoredAttachments { get; init; } = [];
}
```

CLI rendering:
```
Context compacted (TraditionalCompact): 85,432 -> 23,105 tokens (142 -> 28 messages)
  Restored: 2 files, 1 skill
```

## 11. CLI Integration

### Settings Command

```
/config context.max_tokens 200000
/config context.micro_threshold 0.60
/config context.traditional_threshold 0.75
/config context.keep_recent 10
/config context.summary_model anthropic/claude-3-haiku
```

### Manual Compact Command

```
/compact          # Trigger traditional compact manually
/compact status   # Show current context usage
```

## 12. Implementation Roadmap

### Phase 1: Core (Week 1)
- [ ] `ITokenEstimator` + `CharTokenEstimator`
- [ ] `ICompactStrategy` interface
- [ ] `MicroCompactStrategy` (Level 1)
- [ ] Integration into `Agent.RunStreamAsync()`
- [ ] Basic CLI display of compaction events

### Phase 2: Full Compression (Week 2)
- [ ] `TraditionalCompactStrategy` (Level 3)
- [ ] `AttachmentManager` (file/skill restoration)
- [ ] `AgentContextCompactedEvent` + UI rendering
- [ ] `/compact` manual command

### Phase 3: Polish (Week 3)
- [ ] `UsageTracker` with real-time display
- [ ] CLI `/config context.*` settings
- [ ] Session memory extraction (Level 2, optional)
- [ ] Tests: unit + integration

## 13. Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Token estimation | Char-level, no dependency | Fast, accurate enough, zero overhead |
| Summary model | Configurable, default = current model | Can use cheaper model (Haiku) for summaries |
| Compact trigger | Before every LLM call | Guaranteed never exceed window |
| Summary storage | In-memory, session-level | Aligns with `AgentContext.State` |
| Compact granularity | By message rounds, keep recent N | Simple, predictable |
| Image handling | Strip before summarize | Save tokens, images can't be summarized well |
| Prompt cache | MicroCompact preserves structure | Enable cache reuse across compactions |
| Attachment restore | Auto after TraditionalCompact | Prevent loss of critical context (files, skills) |
