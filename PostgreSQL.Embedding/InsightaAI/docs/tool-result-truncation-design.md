# InsightaAI Agent - Tool Result Truncation & Persistence Design

## 1. Background & Problem

Current context management has 3 levels of compression:

| Level | Strategy | Trigger | Action |
|-------|----------|---------|--------|
| L1 | MicroCompactStrategy | 60% utilization | Truncate old tool results with type-specific strategies |
| L2 | SessionMemoryCompactStrategy | 65% utilization | Replace old messages with session memory (zero LLM cost) |
| L3 | TraditionalCompactStrategy | 75% utilization | LLM generates full summary |

**Problems**:
1. **No interception at execution time** - Tool results enter context unchecked, no matter how large
2. **No persistence mechanism** - Large results are either fully in context or truncated/lost
3. **Truncation strategies are centralized** - All in MicroCompactStrategy, not in tools themselves
4. **Fixed thresholds** - Don't adapt to context utilization

Reference: [Claude Code Context Management](https://diwang.info/claude-code-from-scratch/#/docs/07-context)

## 2. Design Goals

### 2.1 Progressive Compression (渐进式压缩)
Use the least expensive approach first, escalate only when necessary:

```
Layer 0:  Execution-time interception                      → Tool.Intercept() at "hot" time
          - Persistence (> 30KB, tool decides)           → Reversible (file on disk)
          - Truncation (> 50K chars, default no-op)      → Irreversible, but re-callable tools
Layer 1:  Budget-aware tightening (existing)           → Adjust thresholds dynamically
Layer 2:  MicroCompact (existing)                      → Clean old tool results
Layer 3:  Session memory compact (existing)            → Replace with session memory
Layer 4:  Traditional compact (existing)               → LLM summary (last resort)
```

**Layer 0 Two-Phase Processing**:
- Phase 1: **Persistence** (30KB threshold, tool-controlled) — write to disk, keep preview in context
- Phase 2: **Truncation** (50K chars threshold, default no-op) — only if result is still too large after persistence

**Key Principle**: Persistence is reversible (file on disk), truncation is not. Always prefer persistence over truncation.

### 2.2 Tool-Owned Truncation
Each tool knows best how to truncate its own results. Truncation logic moves from centralized `MicroCompactStrategy` to individual tools.

### 2.3 Reversibility
Prioritize reversible approaches (persistence) over irreversible ones (truncation).

## 3. Architecture Design

### 3.1 New Components

```
InsightaAI.Agent/
├── Abstractions/
│   ├── IToolExecutor.cs                    # Add Intercept default interface method
│   ├── TruncationContext.cs                # NEW: Context for interception decisions
│   └── InterceptionResult.cs              # NEW: Result of Intercept (content + metadata)
├── Context/
│   └── Compaction/
│       └── MicroCompactStrategy.cs         # Refactored: delegates to tool.Intercept()
└── Tools/
    └── BuiltIn/
        └── FileReadTool.cs                 # Example: override Intercept with persistence
```

### 3.2 Updated Flow

**Before (Current)**:
```
ToolExecutor.ExecuteToolsParallelAsync
  → _handler.Invoke(toolCallRequest)        // Execute tool
  → messages.Add(toolResult.Content)        // Add directly to context
  ... later ...
  → MicroCompactStrategy.CompactAsync()     // Truncate old results
```

**After (New)**:
```
ToolExecutor.ExecuteToolsParallelAsync
  → _handler.Invoke(toolCallRequest)        // Execute tool
  → tool.Intercept(result, ctx)             // Layer 0: Intercept at "hot" time
  → messages.Add(intercepted.Content)       // Add intercepted result to context
  ... later ...
  → MicroCompactStrategy.CompactAsync()     // Skip already-intercepted results
```

## 4. IToolExecutor.Intercept Method

### 4.1 Interface Change

```csharp
public interface IToolExecutor
{
    string Name { get; }
    ToolDefinition Definition { get; }
    
    Task<ToolResult> ExecuteAsync(
        IDictionary<string, object> args,
        ToolExecutionContext context);
    
    /// <summary>
    /// Intercept tool result before adding to context.
    /// Can perform persistence (reversible) or truncation (irreversible).
    /// Default implementation: no-op (return as-is).
    /// Override to apply tool-specific interception logic.
    /// </summary>
    InterceptionResult Intercept(ToolResult result, TruncationContext context)
    {
        return InterceptionResult.NotIntercepted(result);
    }
}
```

### 4.2 InterceptionResult Definition

```csharp
public sealed record InterceptionResult
{
    /// <summary>Intercepted tool result (may be truncated or with preview)</summary>
    public ToolResult Result { get; init; }
    
    /// <summary>Whether this result was intercepted at execution time</summary>
    public bool ToolResultIntercepted { get; init; }
    
    /// <summary>Path to persisted file (if applicable)</summary>
    public string? PersistedPath { get; init; }
    
    /// <summary>Original result length before processing</summary>
    public int OriginalLength { get; init; }
    
    public InterceptionResult(ToolResult result, bool toolResultIntercepted, 
        string? persistedPath = null, int originalLength = 0)
    {
        Result = result;
        ToolResultIntercepted = toolResultIntercepted;
        PersistedPath = persistedPath;
        OriginalLength = originalLength;
    }
    
    /// <summary>Create a non-intercepted result (pass-through)</summary>
    public static InterceptionResult NotIntercepted(ToolResult result) => 
        new(result, toolResultIntercepted: false);
```

### 4.3 Design Rationale

| Aspect | Decision | Reason |
|--------|----------|--------|
| Interface method | Default interface method (C# 8+) | No breaking changes for existing tools |
| Default behavior | No-op | Tools that don't override work unchanged |
| When called | After tool execution, before adding to messages | Results are "hot", processing is most effective |
| Who calls | `ToolExecutor` | Single point of interception |
| Method name | `Intercept` | Intercepts result before entering context |
| Return type | `InterceptionResult` | Carries metadata for MicroCompactStrategy to skip |

## 5. TruncationContext

### 5.1 Definition

```csharp
public sealed record TruncationContext
{
    /// <summary>Original character count of the result</summary>
    public int OriginalLength { get; init; }
    
    /// <summary>Original line count of the result (lazy computed)</summary>
    public Lazy<int> OriginalLineCount { get; init; }
    
    /// <summary>Current context utilization ratio (0.0 ~ 1.0)</summary>
    public double UtilizationRatio { get; init; }
    
    /// <summary>Context budget configuration (read-only)</summary>
    public ContextBudget Budget { get; init; }
    
    /// <summary>Directory for persisting large tool results</summary>
    public string ToolResultDirectory { get; init; }
    
    /// <summary>Tool name (for file naming)</summary>
    public string ToolName { get; init; }
    
    /// <summary>Tool call ID (for file naming uniqueness)</summary>
    public string ToolCallId { get; init; }
    
    /// <summary>Force truncation regardless of thresholds (for emergency)</summary>
    public bool ForceTruncate { get; init; }
}
```

### 5.2 Field Usage by Tool Type

| Tool Type | Primary Dimension | Secondary Dimension | Notes |
|-----------|-------------------|---------------------|-------|
| FileReadTool | `OriginalLineCount` | `OriginalLength` | File content is line-oriented |
| GrepTool | `OriginalLength` | `OriginalLineCount` | Search results vary in structure |
| BashTool | `OriginalLineCount` | `OriginalLength` | Command output is line-oriented |
| WebFetchTool | `OriginalLength` | - | Web content is character-oriented |
| WebSearchTool | `OriginalLength` | - | Search results are character-oriented |
| MCP/External | `OriginalLength` | - | Use default implementation |

**Note**: `OriginalLineCount` is `Lazy<int>` to avoid unnecessary array allocation for tools that don't need line count.

### 5.3 Calculation in ToolExecutor

```csharp
var textBlocks = toolResult.Content.OfType<TextBlock>().ToList();
var totalText = string.Join("\n", textBlocks.Select(t => t.Text));

var truncationContext = new TruncationContext
{
    OriginalLength = totalText.Length,
    OriginalLineCount = new Lazy<int>(() => totalText.Split('\n').Length),
    UtilizationRatio = context.ContextManager?.CurrentBudget?.UtilizationRatio ?? 0,
    Budget = context.ContextManager?.CurrentBudget,
    ToolResultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "InsightaAI", "ToolResults", _sessionId),
    ToolName = toolName,
    ToolCallId = toolCallId,
    ForceTruncate = false
};
```

## 6. ToolResultDirectory

### 6.1 Purpose
Persistent storage for large tool results. Unlike temporary files, these are session-scoped and cleaned up when the session ends.

### 6.2 Directory Structure
```
{LocalAppData}/InsightaAI/ToolResults/{sessionId}/
├── FileRead_20260804_143022.txt
├── Grep_20260804_143155.txt
└── WebFetch_20260804_144521.txt
```

### 6.3 Naming Convention
```
{ToolName}_{yyyyMMdd}_{HHmmss}_{toolCallId}.txt
```

The `toolCallId` suffix ensures uniqueness when multiple tools execute in parallel.

### 6.4 Lifecycle
- **Created**: When tool result exceeds persistence threshold (30KB)
- **Accessed**: When agent needs to re-read full content via `read_file`
- **Cleaned**: When session ends or session memory is cleared

### 6.5 Cleanup Strategy

#### Normal Exit
```csharp
// In ContextManager.DisposeAsync()
if (Directory.Exists(_toolResultDirectory))
{
    Directory.Delete(_toolResultDirectory, recursive: true);
}
```

#### Abnormal Exit / Crash Recovery
On startup, scan for old session directories and clean up:
```csharp
// In Agent initialization
var cutoff = DateTime.UtcNow.AddDays(-7); // Keep for 7 days max
var basePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "InsightaAI", "ToolResults");

if (Directory.Exists(basePath))
{
    foreach (var dir in Directory.EnumerateDirectories(basePath))
    {
        var info = new DirectoryInfo(dir);
        if (info.LastWriteTimeUtc < cutoff)
            Directory.Delete(dir, recursive: true);
    }
}
```

### 6.6 Why Not Temporary Directory?
- `Path.GetTempPath()` is shared across all applications
- Risk of name collisions
- May be cleaned by OS at unexpected times
- Session-scoped directory is more predictable

## 7. Layer 0: Execution-Time Processing

### 7.1 When
Immediately after tool execution, before adding result to messages.

### 7.2 Thresholds
- **Persistence**: 30KB (tool-controlled, reversible)
- **Truncation**: 50K chars (default no-op, irreversible)
- Tools can override with their own thresholds

### 7.3 Default Behavior (IToolExecutor default implementation)
```csharp
InterceptionResult Intercept(ToolResult result, TruncationContext ctx)
{
    // Default: no-op, return as-is
    return InterceptionResult.NotIntercepted(result);
}
```

**Note**: Default is no-op, not truncation. Tools must explicitly opt-in to interception.

### 7.4 Tool-Specific Examples

#### FileReadTool
```csharp
public override InterceptionResult Intercept(ToolResult result, TruncationContext ctx)
{
    var text = result.Content.OfType<TextBlock>().First().Text;
    
    // < 30KB: no action
    if (ctx.OriginalLength <= 30_000) 
        return InterceptionResult.NotIntercepted(result);
    
    // >= 30KB: persist to disk, keep preview in context
    var path = Path.Combine(ctx.ToolResultDirectory, 
        $"FileRead_{DateTime.Now:yyyyMMdd_HHmmss}_{ctx.ToolCallId}.txt");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    
    // Use StreamWriter for large files to avoid OOM
    using (var writer = new StreamWriter(path))
    {
        writer.Write(text);
    }
    
    // Smart preview based on file type (if available from tool args)
    var lineCount = ctx.OriginalLineCount.Value;
    var previewLines = text.Split('\n').Take(200);
    var preview = string.Join("\n", previewLines);
    
    return new InterceptionResult(
        ToolResult.FromText($"{preview}\n\n[完整内容已保存: {path}] (共 {lineCount} 行)"),
        toolResultIntercepted: true,
        persistedPath: path,
        originalLength: ctx.OriginalLength
    );
}
```

#### GrepTool
```csharp
public override InterceptionResult Intercept(ToolResult result, TruncationContext ctx)
{
    var text = result.Content.OfType<TextBlock>().First().Text;
    
    if (ctx.OriginalLength <= 30_000) 
        return InterceptionResult.NotIntercepted(result);
    
    // Search results: keep file names only (more robust pattern)
    var lines = text.Split('\n');
    var fileNames = lines.Where(l => l.StartsWith("=== ") || l.Contains(":")).ToList();
    
    // Fallback: if no pattern matches, keep first 100 lines
    if (fileNames.Count == 0)
        fileNames = lines.Take(100).ToList();
    
    return new InterceptionResult(
        ToolResult.FromText($"[搜索结果过大，已截断。匹配文件: {fileNames.Count} 个]\n" 
            + string.Join("\n", fileNames)),
        toolResultIntercepted: true,
        originalLength: ctx.OriginalLength
    );
}
```

#### BashTool
```csharp
public override InterceptionResult Intercept(ToolResult result, TruncationContext ctx)
{
    var text = result.Content.OfType<TextBlock>().First().Text;
    
    if (ctx.OriginalLength <= 30_000) 
        return InterceptionResult.NotIntercepted(result);
    
    // Keep head (first 50 lines) + tail (last 50 lines)
    // This preserves both the beginning context and the final result
    var lines = text.Split('\n');
    var head = lines.Take(50);
    var tail = lines.TakeLast(50);
    var truncated = string.Join("\n", head) 
        + $"\n\n[... 截断 {lines.Length - 100} 行 ...]\n\n" 
        + string.Join("\n", tail);
    
    return new InterceptionResult(
        ToolResult.FromText(truncated),
        toolResultIntercepted: true,
        originalLength: ctx.OriginalLength
    );
}
```

**Design Notes**:
- **FileReadTool**: Persists to disk (reversible), keeps 200-line preview
- **GrepTool**: Keeps file names only (more robust than `===` pattern)
- **BashTool**: Keeps head + tail (preserves context and result)

## 8. MicroCompactStrategy Refactoring

### 8.1 Current State
- Holds 10+ truncation strategies in `CompactableTools` dictionary
- Each strategy knows how to truncate a specific tool type
- Strategies are classes implementing `ToolTruncationStrategy`

### 8.2 New State
- Remove `CompactableTools` dictionary
- For tools that implement `Intercept`: delegate to tool
- For tools that don't: use default FallbackTruncationStrategy
- **Skip results that were already intercepted at execution time** (avoid double truncation)

### 8.3 Refactored Code
```csharp
public sealed class MicroCompactStrategy : ICompactStrategy
{
    // ... existing ShouldCompact, FindToolPairs methods ...
    
    public async Task<CompactionResult> CompactAsync(
        List<Message> messages,
        ContextBudget budget,
        ITokenEstimator tokenEstimator,
        int preCompactTokens,
        CancellationToken cancellationToken = default)
    {
        var toolPairs = FindToolPairs(messages);
        toolPairs.Reverse();
        
        int compactedCount = 0;
        
        for (int i = 0; i < toolPairs.Count; i++)
        {
            var (toolUseIndex, toolResultIndex, toolName) = toolPairs[i];
            
            if (i < budget.KeepRecentToolResults) continue;
            
            // Skip results that were already intercepted at execution time
            if (messages[toolResultIndex].ToolResultIntercepted)
                continue;
            
            // Try to get tool executor from DI
            var tool = GetToolExecutor(toolName);
            
            if (tool != null)
            {
                // Tool has Intercept: delegate
                var result = messages[toolResultIndex];
                var truncationContext = CreateTruncationContext(result, budget);
                
                // Preserve original IsError state
                var toolResult = new ToolResult 
                { 
                    Content = result.Content, 
                    IsError = result.IsError 
                };
                
                var intercepted = tool.Intercept(toolResult, truncationContext);
                
                messages[toolResultIndex] = new Message
                {
                    Role = MessageRole.ToolResult,
                    ToolCallId = result.ToolCallId,
                    ToolName = result.ToolName,
                    Content = intercepted.Result.Content,
                    ToolResultIntercepted = intercepted.ToolResultIntercepted
                };
            }
            else
            {
                // No tool found: use default truncation
                ApplyDefaultTruncation(messages, toolResultIndex);
            }
            
            compactedCount++;
        }
        
        // ... rest of method ...
    }
}
```

### 8.4 Migration Path
1. Keep existing `ToolTruncationStrategy` classes temporarily
2. Add `Intercept` to tools incrementally
3. Once all tools have `Intercept`, remove `ToolTruncationStrategy` classes
4. MicroCompactStrategy becomes thin delegation layer

### 8.5 Feature Flag
Add a feature flag to enable/disable the new interception behavior:
```csharp
// In ContextManager or Agent configuration
public bool EnableToolResultInterception { get; set; } = true;

// In ToolExecutor
if (_enableToolResultInterception)
{
    var intercepted = tool.Intercept(toolResult, truncationContext);
    // ...
}
```

## 9. Implementation Roadmap

### Phase 1: Core Infrastructure ✅ Done
- [x] Add `TruncationContext` class
- [x] Add `InterceptionResult` class
- [x] Add `Intercept` default interface method to `IToolExecutor`
- [x] Update `ToolExecutor` to call `Intercept` after execution
- [x] Add `ToolResultDirectory` management to `ContextManager` — 通过 `TruncationContext.ToolResultDirectory` 传入
- [x] Add `ToolResultIntercepted` flag to `Message` class
- [x] Add feature flag: `EnableToolResultInterception`（通过构造参数控制）

### Phase 2: Built-in Tool Overrides ✅ Done
- [x] `FileReadTool.Intercept` — 持久化 + 200 行预览
- [x] `GrepTool.Intercept` — 文件名 + 匹配数量
- [x] `BashTool.Intercept` — 头尾各 50 行
- [x] `WebFetchTool.Intercept` — 重构为 IToolExecutor + 持久化 + 5000 字符预览
- [x] `WebSearchTool.Intercept` — 重构为 IToolExecutor + 10000 字符预览
- [ ] **Future Enhancement**: Smart preview by file type (JSON, Markdown, etc.)

### Phase 3: MicroCompactStrategy Refactoring ✅ Done
- [x] Refactor `MicroCompactStrategy` to delegate to `tool.Intercept()`
- [x] Add skip logic for already-intercepted results
- [x] Keep `FallbackTruncationStrategy` for tools without overrides
- [ ] Remove redundant `ToolTruncationStrategy` classes — 可选清理

### Phase 4: Testing & Polish ⬜ Not Started
- [ ] Unit tests for each tool's `Intercept`
- [ ] Integration test: large file read → persistence → re-read
- [ ] Integration test: context utilization triggers appropriate layers
- [ ] CLI display: show when results are truncated/persisted
- [ ] Add monitoring metrics: truncation frequency, persistence frequency

### Phase 5: Cleanup & Documentation ⬜ Not Started
- [ ] Implement ToolResultDirectory cleanup (normal + abnormal exit)
- [ ] Update user documentation
- [ ] Performance benchmarking

**Rollback Plan**:
- Feature flag `EnableToolResultInterception` allows quick disable
- Keep old `ToolTruncationStrategy` classes until Phase 3 is stable
- If issues arise, set flag to `false` to revert to old behavior

## 10. Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Where to process | In `ToolExecutor`, after execution | Results are "hot", most effective |
| Who owns processing logic | Individual tools | Tools know their data structure best |
| Default behavior | No-op | No breaking changes for existing tools |
| Primary dimension | Character count | Good token approximation, O(1) cost |
| Secondary dimension | Line count (lazy) | Avoids unnecessary array allocation |
| Persistence directory | Session-scoped, not temp | More predictable, no collisions |
| Persistence threshold | 30KB | Balance between context savings and re-read cost |
| Truncation threshold | 50K chars | Prevents any single result from dominating context |
| MicroCompactStrategy role | Delegation layer | Still needed as fallback for non-overridden tools |
| Method naming | `Intercept` | Intercepts result before entering context |
| Return type | `InterceptionResult` | Carries metadata for MicroCompactStrategy to skip |
| Double truncation prevention | `ToolResultIntercepted` flag | Avoids intercepting same result twice |
| Feature flag | `EnableToolResultInterception` | Allows gradual rollout and quick rollback |
| Cleanup strategy | Normal exit + 7-day retention | Handles both normal and abnormal exits |
| Concurrency safety | ToolCallId in filename | Prevents file name conflicts in parallel execution
