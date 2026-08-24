# Session Memory Hook LLM 增强设计

> 在 Hook 级别引入 LLM，实现增量式会话摘要，替代当前的关键词匹配方案。
> 参考：[孔某人《主流Agent Harness实现对比——Context压缩》](./主流Agent%20Harness实现对比——Context压缩.txt)

**版本历史**：
- v1 (2025-06-23): 初始设计
- v2 (2025-06-24): 更新以反映实际实现（Options 模式、fire-and-forget、嵌入式 Prompt、线程安全快照）

---

## 1. 动机与目标

### 当前问题

```
SessionMemoryHook 目前使用关键词匹配（ExtractKeyInformation）
    ↓
提取质量粗糙（"我喜欢"、"项目" 等简单模式匹配）
    ↓
SessionMemoryCompactStrategy 在 65% 阈值时读 session-memory.md
    ↓
摘要质量差，信息丢失严重
```

### 改进目标

- **每轮增量总结**：每轮对话结束后，Hook 用 LLM 将关键信息合并到 session-memory.md
- **零等待压缩**：到阈值时直接读高质量摘要，无需再调 LLM
- **成本可控**：增量摘要 prompt 短，使用低 Temperature；支持配置调用频率
- **降级安全**：LLM 不可用时自动退回关键词匹配

---

## 2. 架构变更

### 2.1 扩展 `ToolExecutionContext`

在已有的 `ToolExecutionContext` 中暴露 `LlmClient`，使 ToolHook 也能调用 LLM。

```csharp
public sealed record ToolExecutionContext
{
    // ... 现有属性

    /// <summary>LLM 客户端，ToolHook 可调用 LLM 生成内容</summary>
    public ILlmClient? LlmClient { get; init; }
}
```

### 2.2 新增 `HookContext`

AgentHook 专用上下文，承载 LLM 通道和会话元数据。

```csharp
/// <summary>
/// AgentHook 执行上下文，承载 LLM 通道和会话级元数据。
/// </summary>
public sealed record HookContext
{
    /// <summary>LLM 客户端（可选，测试场景可为 null）</summary>
    public ILlmClient? LlmClient { get; init; }

    /// <summary>当前会话 ID</summary>
    public required string SessionId { get; init; }
}
```

> **v2 变更**：`LlmClient` 改为可选（`ILlmClient?`），便于单元测试时传入 null。

### 2.3 改造 `IAgentHook` 接口

直接修改接口签名，加入 `HookContext`（当前只有一个实现，无需兼容旧签名）。

```csharp
public interface IAgentHook
{
    string Id { get; }

    Task OnRoundStartAsync(
        string message,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task OnRoundEndAsync(
        HookContext context,
        int round,
        IReadOnlyList<Message> messages,
        Message? assistantMessage,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task OnSessionEndAsync(
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

### 2.4 调用链路变更

```
Agent.RunStreamAsync()
  │
  ├─ 创建 HookContext（注入 LlmClient, SessionId）
  │
  ├─ hook.OnRoundEndAsync(context, round, messages, assistantMessage, ct)
  │    │
  │    └─ SessionMemoryHook
  │         ├─ 创建 messages 快照（线程安全）
  │         ├─ fire-and-forget: Task.Run(async () => ...)
  │         │    ├─ round >= minRounds && round % interval == 0
  │         │    │    └─ LLM 锚定增量摘要 → 写入 session-memory.md
  │         │    └─ 否则
  │         │         └─ 关键词提取 → 写入 session-memory.md
  │         └─ 立即返回 Task.CompletedTask（不阻塞 Agent 主循环）
  │
  └─ ContextManager.CompactAsync()
       └─ SessionMemoryCompactStrategy
            └─ 读取 session-memory.md（质量已高，无需再调 LLM）
```

> **v2 变更**：
> 1. **Fire-and-forget**：`OnRoundEndAsync` 使用 `Task.Run` 在后台执行摘要提取，立即返回 `Task.CompletedTask`，不阻塞 Agent 主循环。用户无需等待摘要完成即可看到响应。
> 2. **线程安全快照**：在 `Task.Run` 前调用 `messages.ToList()` 创建快照，避免后台任务读取被主循环修改的消息列表。

---

## 3. SessionMemoryHook 改造

### 3.1 结构化摘要格式

采用 Open Code 和 Pi 的结构化 Markdown 格式（参考文章第 5、6 节），替代当前自由文本。

> **v2 变更**：摘要 Prompt 已提取为嵌入式资源 `Prompts/anchored-summary.txt`，通过 `PromptLoader.Load("anchored-summary")` 加载。模板包含 8 个 section：Goal、Constraints & Preferences、Progress (Done/In Progress/Blocked)、Key Decisions、Next Steps、Critical Context、Relevant Files、Active Context。

```markdown
## Goal
- [一句话的任务摘要；会话涵盖不同任务时可以是多项]

## Constraints & Preferences
- [用户的约束、偏好或规格]
- [(none)]

## Progress
### Done
- [x] [已完成的任务/改动]

### In Progress
- [ ] [当前的工作]

### Blocked
- [阻碍进展的问题，如果有的话]

## Key Decisions
- **[决策]**: [简要理由]

## Next Steps
1. [按顺序列出下一步动作]

## Critical Context
- [重要的技术事实、错误、待解问题、文件路径、函数名]
- [(none)]

## Relevant Files
- [文件或目录路径：为何重要]
- [(none)]

## Active Context
- [当前正在处理的文件、函数或代码段]
```

**规则**（参考 Open Code 的设定）：
- 保留每一节，即使为空也要保留
- 使用简短的要点，而非散文式段落
- 原样保留确切的文件路径、命令、错误串和标识符
- 不要提及摘要过程或上下文被压缩

### 3.2 增量摘要流程

采用 **Open Code 的锚定摘要（Anchored Summary）方案**（参考文章第 5 节），
结合 **Claude Code 的两步式 Prompt**（先 `<analysis>` 再 `<summary>`）提高摘要质量。

> **v2 变更**：
> 1. **嵌入式 Prompt**：完整 Prompt 已提取到 `Prompts/anchored-summary.txt`，通过 `PromptLoader.Load("anchored-summary")` 加载，不再硬编码在代码中。
> 2. **API 层面禁止工具调用**：除了 Prompt 约束外，代码显式设置 `Tools = []` 和 `ToolChoice = ToolChoiceMode.None`。
> 3. **Fire-and-forget**：摘要提取在 `Task.Run` 中异步执行，不阻塞 Agent 主循环。

```
OnRoundEndAsync(context, round, messages, assistantMessage) 触发:

  Step 0: 创建 messages 快照（messages.ToList()）
          - 确保后台任务读取的数据不被主循环修改

  Step 1: fire-and-forget: Task.Run(async () => ...)

  Step 2: 检查是否需要 LLM 摘要
          - _options.EnableLlmSummary == true
          - round >= _options.MinRoundsBeforeLlm
          - (round - _options.MinRoundsBeforeLlm) % _options.SummaryInterval == 0
          - context.LlmClient != null
          - 不满足 → 降级到关键词匹配

  Step 3: 读取 session-memory.md → 获取 existingSummary
          （如果文件不存在或无内容，则为空）

  Step 4: 加载嵌入式 Prompt
          - PromptLoader.Load("anchored-summary")
          - 替换 {CONVERSATION} 和 {PREVIOUS_SUMMARY} 占位符

  Step 5: 调用 LlmClient.CompleteAsync()
          - Model: _options.SummaryModel
          - Temperature: _options.SummaryTemperature (默认 0.3)
          - MaxTokens: _options.SummaryMaxTokens (默认 512)
          - Tools: []（不提供工具）
          - ToolChoice: ToolChoiceMode.None（API 层面禁止）

  Step 6: 从响应中提取 <summary> 标签内容
          - 正则匹配：<summary>...</summary>
          - 无标签时使用完整响应（截断到 1000 字符）

  Step 7: 替换写入 session-memory.md（不是追加）

  Step 8: 如果 LLM 调用失败 → 降级到关键词匹配（追加模式）
```

> **设计决策**：
> - **两步式 Prompt**：参考 Claude Code，先 `<analysis>` 再 `<summary>`，强制 LLM 梳理思路后再输出，提高摘要质量
> - **禁止工具调用**：Prompt 约束 + API 层面 `Tools=[]` + `ToolChoice=None`，双重保障
> - **增量更新**：参考 Open Code，读旧摘要 → 合并新信息 → 替换写入，多次压缩不丢失信息
> - **嵌入式资源**：Prompt 模板存储为 `.txt` 文件，通过 `Assembly.GetManifestResourceStream` 加载，便于维护和版本控制

### 3.3 类变更示意

> **v2 变更**：采用 `SessionMemoryOptions` record 封装配置，减少构造函数参数数量。Prompt 已提取到嵌入式资源文件。

```csharp
/// <summary>
/// SessionMemoryHook 配置选项
/// </summary>
public sealed record SessionMemoryOptions
{
    public bool EnableLlmSummary { get; init; } = true;
    public int MinRoundsBeforeLlm { get; init; } = 3;
    public int SummaryInterval { get; init; } = 1;
    public string SummaryModel { get; init; } = "deepseek-v4-flash";
    public int SummaryMaxTokens { get; init; } = 512;
    public double SummaryTemperature { get; init; } = 0.3;
}

public sealed class SessionMemoryHook : IAgentHook
{
    private readonly string _sessionId;
    private readonly string _userId;
    private readonly string? _projectId;
    private readonly string _sessionDir;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly SessionMemoryOptions _options;

    public string Id => "session-memory";
    public string SessionId => _sessionId;
    public string SessionDirectory => _sessionDir;

    public SessionMemoryHook(
        string sessionId,
        string userId,
        string? projectId = null,
        SessionMemoryOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentException.ThrowIfNullOrEmpty(userId);

        _options = options ?? new SessionMemoryOptions();

        if (_options.SummaryInterval < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "SummaryInterval must be >= 1");
        if (_options.SummaryMaxTokens < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "SummaryMaxTokens must be >= 1");

        _sessionId = sessionId;
        _userId = userId;
        _projectId = projectId;

        // 会话记忆目录
        var memoryBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".insightai", "memory", "sessions", sessionId);
        _sessionDir = memoryBase;
        Directory.CreateDirectory(_sessionDir);
    }

    public Task OnRoundEndAsync(
        HookContext context,
        int round,
        IReadOnlyList<Message> messages,
        Message? assistantMessage,
        CancellationToken cancellationToken = default)
    {
        // 创建快照：后台任务可能在 Agent 主循环修改 messages 之后才执行
        var messagesSnapshot = messages.ToList();

        // 在后台执行提取，不传递 cancellationToken（调用方可能已取消）
        _ = Task.Run(async () =>
        {
            try
            {
                await ExtractAndSaveMemoryAsync(context, round, messagesSnapshot, assistantMessage, CancellationToken.None);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SessionMemory] Round {round} extraction failed: {ex.Message}");
            }
        });

        // 立即返回，不等待后台任务完成
        return Task.CompletedTask;
    }

    // LLM 增量摘要（基于 Open Code 的 Anchored Summary）
    private async Task ExtractAndSaveMemoryAsync(
        HookContext context,
        int round,
        IReadOnlyList<Message> messages,
        Message? assistantMessage,
        CancellationToken cancellationToken)
    {
        // 检查是否满足 LLM 摘要条件
        if (_options.EnableLlmSummary
            && round >= _options.MinRoundsBeforeLlm
            && (round - _options.MinRoundsBeforeLlm) % _options.SummaryInterval == 0
            && context.LlmClient != null)
        {
            // 读取已有摘要
            var existingSummary = await GetSessionMemoryAsync(cancellationToken);

            // 使用 LLM 锚定增量摘要
            var mergedSummary = await GenerateLlmSummaryAsync(
                context.LlmClient, existingSummary, messages, cancellationToken);

            if (!string.IsNullOrWhiteSpace(mergedSummary))
            {
                // 替换文件（不是追加）
                await _lock.WaitAsync(cancellationToken);
                try
                {
                    var memoryPath = Path.Combine(_sessionDir, "session-memory.md");
                    await File.WriteAllTextAsync(memoryPath, mergedSummary, cancellationToken);
                }
                finally
                {
                    _lock.Release();
                }
                return;
            }

            // LLM 失败，降级到关键词提取
        }

        // 降级路径：关键词提取（追加模式）
        var keywordSummary = ExtractRoundInfo(round, messages, assistantMessage);
        if (string.IsNullOrWhiteSpace(keywordSummary))
            return;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // ... 追加到文件 ...
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string> GenerateLlmSummaryAsync(
        ILlmClient llmClient,
        string existingSummary,
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken)
    {
        // 加载嵌入式 Prompt
        var promptTemplate = PromptLoader.Load("anchored-summary");
        var previousSummary = string.IsNullOrEmpty(existingSummary) ? "(none)" : existingSummary;

        // 构建对话文本
        var recentMessages = messages.TakeLast(10).ToList();
        var conversationText = new StringBuilder();
        foreach (var msg in recentMessages)
        {
            var role = msg.Role switch
            {
                MessageRole.User => "User",
                MessageRole.Assistant => "Assistant",
                MessageRole.System => "System",
                _ => msg.Role.ToString()
            };
            var content = msg.GetTextContent();
            if (!string.IsNullOrWhiteSpace(content))
            {
                if (content.Length > 2000)
                    content = content[..2000] + "...";
                conversationText.AppendLine($"{role}: {content}");
            }
        }

        var prompt = promptTemplate
            .Replace("{CONVERSATION}", conversationText.ToString())
            .Replace("{PREVIOUS_SUMMARY}", previousSummary);

        var request = new LlmRequest
        {
            Model = _options.SummaryModel,
            Messages = [Message.FromSystem(prompt)],
            Tools = [],
            ToolChoice = ToolChoiceMode.None,
            MaxTokens = _options.SummaryMaxTokens,
            Temperature = _options.SummaryTemperature
        };

        var response = await llmClient.CompleteAsync(request, cancellationToken);
        var responseText = response?.GetTextContent();

        return ExtractSummary(responseText ?? "");
    }

    // 从 LLM 响应中提取 <summary> 标签内容
    private static string ExtractSummary(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "";

        var match = Regex.Match(response, @"<summary>\s*(.*?)\s*</summary>", RegexOptions.Singleline);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return response.Length > 1000 ? response[..1000] : response;
    }
}
```

### 3.4 配置方式

> **v2 变更**：采用 `SessionMemoryOptions` record 封装配置，构造函数参数从 9 个减少到 4 个。

LLM 摘要相关配置通过 `SessionMemoryOptions` 传入：

```csharp
// ChatCommand.cs 中创建 Hook
var memoryOptions = new SessionMemoryOptions
{
    SummaryModel = config.Model  // 跟随当前 LLM adapter 模型
};
var sessionMemoryHook = new SessionMemoryHook(sessionId, userId, options: memoryOptions);
```

| 参数 | 默认值 | 作用 |
|------|--------|------|
| `EnableLlmSummary` | `true` | 总开关，关闭后退回关键词匹配 |
| `MinRoundsBeforeLlm` | `3` | 前几轮对话信息少，用关键词就够了 |
| `SummaryInterval` | `1` | 长对话可以设成 2 或 3，减少 LLM 调用 |
| `SummaryModel` | `"deepseek-v4-flash"` | 摘要使用的模型，默认跟随主模型 |
| `SummaryMaxTokens` | `512` | 摘要最大 token 数，控制成本 |
| `SummaryTemperature` | `0.3` | 低温度，更确定性的摘要输出 |

---

## 4. 与现有压缩策略的关系

### 4.1 交互流程

```
                  每轮结束后
                     │
                     ▼
    SessionMemoryHook.OnRoundEndAsync(context, ...)
                     │
                     ├── LLM 条件满足 → 锚定增量摘要 → 写入 session-memory.md
                     │
                     └── LLM 条件不满足或失败 → 关键词提取 → 写入 session-memory.md

                 Context 达到 65% 阈值
                     │
                     ▼
    SessionMemoryCompactStrategy.CompactAsync()
                     │
                     ├── 读取 session-memory.md（结构化、高质量）
                     ├── 保留最近 N 轮完整消息
                     ├── 插入压缩边界标记
                     └── 执行上下文替换（零 LLM 成本）
```

### 4.2 压缩后的导入 Prompt

> **v2 变更**：导入 Prompt 已提取为嵌入式资源 `Prompts/compaction-import.txt`，通过 `PromptLoader.Load("compaction-import")` 加载。

参考 **Hermes Agent 的导入 Prompt**（文章第 7 节），在压缩后插入一条系统消息，明确优先级规则。

文章指出，Hermes 做得好的地方在于明确告诉模型：
- **最新消息 WINS**：覆盖摘要中的旧任务
- **反转信号立即生效**："停下"、"撤销"、"算了" → 终止旧工作
- **持久化记忆始终权威**：MEMORY.md / USER.md 不受压缩影响

```csharp
// SessionMemoryCompactStrategy.cs
private static string BuildCompactionImportPrompt()
{
    return PromptLoader.Load("compaction-import");
}
```

导入提示的核心内容（`compaction-import.txt`）：

```
[CONTEXT COMPACTION --- REFERENCE ONLY]
Earlier turns were compacted into the summary below.
This is a handoff from a previous context window ---
treat it as background reference, NOT as active instructions.

Priority rules:
1. Latest message WINS: overrides stale tasks in the summary
2. Reverse signals take effect immediately: "stop", "undo", "never mind"
3. Persistent memory is always authoritative: MEMORY.md / USER.md
```

### 4.3 对 TraditionalCompact 的影响

> **v2 变更**：TraditionalCompact 保持独立，不复用 session-memory.md。两个策略职责分离：
> - **SessionMemoryCompact**：基于预提取的会话记忆，零 LLM 成本
> - **TraditionalCompact**：基于 LLM 全量摘要，作为最后兜底

| 场景 | TraditionalCompact 行为 |
|------|----------------------|
| LLM 摘要正常 | 通常不会触发（SessionMemoryCompact 已释放足够空间） |
| LLM 摘要正常但对话极长 | 仍可能触发，此时 TraditionalCompact 执行独立的 LLM 摘要 |
| LLM 摘要不可用 | 按原有逻辑执行，TraditionalCompact 使用 LLM 生成摘要 |

**设计决策**：TraditionalCompact 不复用 session-memory.md，原因：
1. session-memory.md 是增量更新的，记录"重要信息"而非"全部历史"
2. 直接复用可能丢失不在摘要中的上下文
3. 两个策略保持独立，降低耦合度

```csharp
// TraditionalCompactStrategy.cs - 始终使用 LLM 生成摘要
var summary = await GenerateSummaryAsync(strippedOldMessages, cancellationToken);
```

---

## 5. 参考来源

本文设计参考了孔某人在《主流Agent Harness实现对比——Context压缩》中分析的各家 Harness 实现：

| 设计点 | 参考来源 | 文章章节 |
|--------|---------|---------|
| 锚定增量摘要（Anchored Summary） | **Open Code** | 第 5 节 |
| 结构化摘要格式（Goal / Progress / Decisions） | **Open Code / Pi** | 第 5、6 节 |
| 两步式 Prompt（analysis + summary） | **Claude Code** | 第 3 节 |
| 禁止工具调用约束 | **Claude Code** | 第 3 节 |
| 压缩后导入提示（优先级规则） | **Hermes Agent** | 第 7 节 |
| 增量更新逻辑（多次压缩不丢失信息） | **Open Code / Pi** | 第 5、6 节 |
| 工具结果清理（MicroCompact） | **已有实现** | — |
| 全量 LLM 摘要（TraditionalCompact 兜底） | **Claude Code / Codex inline** | 第 3、4 节 |

---

## 6. 实施步骤

> **v2 变更**：已更新为实际实现的文件清单。所有步骤均已完成。

### Step 1: 定义新类型

```
新增文件:
  src/InsightaAI.Agent/Hooks/HookContext.cs       ← HookContext record (LlmClient 可选)

改动文件:
  src/InsightaAI.Agent/Hooks/IAgentHook.cs         ← 添加 Id 属性、OnRoundStartAsync 方法
  src/InsightaAI.Agent/Abstractions/ToolExecutionContext.cs  ← 加 LlmClient 属性
```

### Step 2: 修改调用方

```
改动文件:
  src/InsightaAI.Agent/Agent.cs  ←
    - 创建 HookContext（注入 LlmClient, SessionId）
    - 传入 OnRoundEndAsync
```

### Step 3: 改造 SessionMemoryHook

```
新增文件:
  src/InsightaAI.Agent/Memory/SessionMemoryHook.cs  ←
    - SessionMemoryOptions record（配置封装）
    - SessionMemoryHook 完整重写
    - Fire-and-forget + 线程安全快照
    - 锚定增量摘要（读旧 → 合并 → 替换写入）
    - 结构化 Markdown 模板（8 节）
    - 关键词降级路径

改动文件:
  src/InsightaAI.Agent.Cli/Commands/ChatCommand.cs  ←
    - 创建 SessionMemoryOptions
    - 注册 SessionMemoryCompactStrategy
```

### Step 4: 提取 Prompt 为嵌入式资源

```
新增文件:
  src/InsightaAI.Agent/Prompts/PromptLoader.cs        ← 嵌入资源加载器
  src/InsightaAI.Agent/Prompts/anchored-summary.txt   ← 锚定摘要 Prompt
  src/InsightaAI.Agent/Prompts/compaction-import.txt   ← 压缩导入 Prompt
  src/InsightaAI.Agent/Prompts/traditional-summary.txt ← 传统摘要 Prompt

改动文件:
  src/InsightaAI.Agent/InsightaAI.Agent.csproj  ← 添加 EmbeddedResource 配置
```

### Step 5: 添加压缩后导入提示

```
改动文件:
  src/InsightaAI.Agent/Context/SessionMemoryCompactStrategy.cs  ←
    - 使用 PromptLoader.Load("compaction-import")
    - 压缩替换时插入 Hermes 风格的导入提示
```

### Step 6: 测试

```
新增文件:
  tests/InsightaAI.Agent.Tests/Memory/SessionMemoryHookLlmTests.cs  ← 8 个 LLM 路径测试
  tests/InsightaAI.Agent.Tests/Memory/SessionMemoryHookTests.cs     ← 原有测试更新

改动文件:
  tests/InsightaAI.Agent.Tests/Context/SessionMemoryCompactStrategyTests.cs  ← 适配新构造函数
```

### Step 7: 代码审查修复

```
SessionMemoryHook.cs:
  - GetTextContent() NRE 防护（response?.GetTextContent()）
  - 参数验证（ThrowIfNullOrEmpty, ThrowIfLessThan）
  - Options 模式重构（SessionMemoryOptions record）
  - 线程安全快照（messages.ToList()）

MetaLearningStore.cs:
  - 移除未使用的 ConsolidateAsync 方法
```

---

## 7. 风险和缓解

> **v2 变更**：更新风险缓解措施，反映实际实现。

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| 每轮额外 LLM 调用增加成本 | 每轮约 500-800 tokens 输入 + 512 tokens 输出 | MaxTokens=512, Temperature=0.3 控制；SummaryModel 可用更便宜模型；summaryInterval 可调 |
| LLM 调用延迟影响用户体验 | ~~Hook 是同步执行，会阻塞下一轮~~ | **已解决**：Fire-and-forget 模式，Hook 立即返回，摘要在后台异步执行 |
| 线程安全问题 | Agent 主循环修改 messages 时后台任务正在读取 | **已解决**：`messages.ToList()` 创建快照，后台任务使用独立副本 |
| LLM 响应为 null | GetTextContent() 返回 null 导致 NRE | **已解决**：`response?.GetTextContent()` + null coalescing |
| summaryInterval=0 导致除零异常 | 构造函数参数验证缺失 | **已解决**：`ArgumentOutOfRangeException.ThrowIfLessThan(summaryInterval, 1)` |
| LLM 生成不稳定摘要 | 摘要质量波动 | Temperature=0.3；两步式 Prompt 约束输出；异常时降级关键词 |
| 摘要膨胀 | session-memory.md 越来越大 | Prompt 约束 500 词以内；结构化格式天然精简 |
| Token 估算不精确 | 压缩时机不准 | 原文指出所有框架都用 bytes/4 估算，当前实现一致；未来可接入 tokenizer |

---

## 8. 未来方向

- **Micro Compaction 的微调**：参考 Claude Code 的 microcompact 策略，按时间（60min）而非单纯按阈值清理工具结果
- **预计算压缩（precomputedCompact）**：参考 Claude Code 的后台预压缩（第 3 节），进一步减少用户等待
- **响应式兜底压缩（reactiveCompact）**：当服务端报超过 context 限制时触发，对更短的历史进行压缩 + 后面未压缩的部分（参考 Claude Code 第 3 节）
- **树形上下文分支**：参考 Claude Code 的 "Summarize from here"（第 3 节），支持回退到历史分支继续探索
- **Nonlinear Context**：参考孔文综述中提到的 SubAgent 作为隐式压缩的方向
