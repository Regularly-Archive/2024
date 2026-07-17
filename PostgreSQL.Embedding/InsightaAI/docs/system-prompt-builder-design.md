# InsightaAI Agent - SystemPromptBuilder Design Document

## 1. Background & Problem

当前 system prompt 构建分散在多处，存在以下问题：

### 1.1 构建是一次性的

`Agent.RunStreamAsync()` 启动时拼接一次 system prompt：

```
_config.SystemPrompt + skills 列表 + MCP 列表 + memory 索引
→ 一条 SystemMessage 放入 LoopContext
```

此后不再重建。当 skill 被激活、memory 更新时，无法反映到 system prompt 中。

### 1.2 Skill Instructions 增量追加导致膨胀

激活 skill 时，instructions 累积到 `_skillInstructions`：

```csharp
// Agent.RegisterActivateSkillTool()
_skillInstructions += "\n\n" + skill.Instructions;
```

每轮 `AgentLoop` 再把这些 instructions append 到 system message 末尾：

```csharp
// AgentLoop line 93-96
var skillInstructions = _getSkillInstructions();
var updatedSystemMessage = Message.FromSystem(
    requestMessages[0].GetTextContent() + skillInstructions);
```

多轮对话后，已激活 skill 的 instructions 与每轮的增量追加叠加，system prompt 无限制膨胀，破坏 prompt cache。

### 1.3 Skills 列表不做去重

可用 skills 列表列出所有 skills。已激活的 skill 仍出现在列表中，同时 instructions 又被追加到末尾，信息冗余。

### 1.4 Compact 不一致

`CompactContextAsync` 只使用 `_config.SystemPrompt`，不包含 skills/MCP/memory，与 `RunStreamAsync` 行为不一致。

## 2. Design Goals

1. **Builder 模式**：纯组装逻辑，无状态，由调用方传入参数
2. **每轮重建**：发送 LLM 请求前重建 system prompt，保证是最新状态
3. **Skills 去重**：已激活 skill 不在 available 列表中，其 instructions 放入激活段落
4. **Compact 兼容**：compact 时 system prompt 保留不动，只压缩对话历史
5. **单条 SystemMessage**：抹平模型差异（Anthropic 只支持一条 system）

## 3. Architecture

```
SystemPromptBuilder.Build(params)
├── BasePrompt (section: system_prompt_body)
├── AvailableSkills (section: available_skills, excludes activated)
├── AvailableMcps  (section: available_mcps)
├── MemoryIndex    (section: available_memories)
└── ActivatedSkills (section: activated_skills, instructions only)
```

### 3.1 参数对象

```csharp
public sealed record SystemPromptParams
{
    public required string BasePrompt { get; init; }

    // 全部可用 skills（builder 内部排除已激活的）
    public IReadOnlyList<SkillInfo>? AllSkills { get; init; }

    // 已激活的 skills（包含 name + instructions）
    public IReadOnlyList<SkillDescriptor>? ActivatedSkills { get; init; }

    // 可用 MCP 服务器
    public IReadOnlyList<McpServerMetadata>? McpServers { get; init; }

    // Memory 索引文本
    public string? MemoryIndex { get; init; }
}
```

### 3.2 Builder 接口

```csharp
public static class SystemPromptBuilder
{
    /// <summary>
    /// 从各部分组装 system prompt。每次调用返回全新的 prompt 文本。
    /// </summary>
    public static string Build(SystemPromptParams p);
}
```

### 3.3 Section 模板

沿用现有 `PromptTemplate.RenderAsync()` 机制。新增一个模板用于已激活 skills：

**`system-prompt-start.txt`**：

```
# 第一部分：基础提示词
{base_prompt}
```

**`available-skills.txt`**：保持不变（但传入的列表已去重）

**`activated-skills.txt`**（新增）：

```
## Activated Skills
The following skills are currently active:

{activated_skills_list}

When using these skills, follow the instructions above.
```

### 3.4 Build 逻辑

```
Build(params):
    result = ""

    // ---- 基础提示词 ----
    result += base_prompt

    // ---- 可用 Skills（排除已激活） ----
    availableSkills = allSkills - activatedSkills
    if availableSkills not empty:
        result += RenderTemplate("available-skills", {skills_list})

    // ---- 可用 MCP ----
    if mcps not empty:
        result += RenderTemplate("available-mcps", {mcp_servers_list})

    // ---- Memory 索引 ----
    if memoryIndex not empty:
        result += RenderTemplate("available-memories", {memory_index})

    // ---- 已激活 Skills 的 Instructions ----
    if activatedSkills not empty:
        activatedText = join(skill.Instructions for each)
        result += RenderTemplate("activated-skills", {activated_skills_list: activatedText})

    return result
```

## 4. Integration

### 4.1 Agent.cs 变更

当前 `RunStreamAsync` 中构建 system prompt 的逻辑（line 471-489）替换为：

```csharp
// 构建 BuildSystemPrompt 回调，传入 AgentLoop
Func<Task<string>> buildSystemPrompt = async () =>
{
    var skills = await GetAvailableSkillsAsync(cancellationToken);
    var mcps = await GetAvailableMcpServersAsync(cancellationToken);
    var memoryIndex = _memoryManager != null
        ? await GetMemoryIndexAsync(cancellationToken)
        : null;

    return SystemPromptBuilder.Build(new SystemPromptParams
    {
        BasePrompt = _config.SystemPrompt,
        AllSkills = skills,
        ActivatedSkills = _activatedSkills,    // 新增字段，替代 _skillInstructions
        McpServers = mcps,
        MemoryIndex = memoryIndex,
    });
};

// 首次构建 system prompt 放入 LoopContext
var systemPrompt = await buildSystemPrompt();
loopContext.AddMessage(Message.FromSystem(systemPrompt));

// 传入 AgentLoop
var agentLoop = new AgentLoop(_config, llmClient, _toolRegistry,
    toolCallExecutor, buildSystemPrompt);
```

### 4.2 AgentLoop 变更

当前 line 90-97 的追加逻辑替换为：

```csharp
// 每轮重建 system prompt
var systemPrompt = await _buildSystemPrompt(cancellationToken);
var messages = context.Messages.ToList();
if (messages.Count > 0 && messages[0].Role == MessageRole.System)
{
    messages[0] = Message.FromSystem(systemPrompt);
}

var requestMessages = messages.ToArray();
```

### 4.3 激活 Skill 时

```csharp
// RegisterActivateSkillTool()
_activatedSkills.Add(new SkillDescriptor(skill.Name, skill.Instructions));
// 下次 BuildSystemPrompt 自动包含
```

去掉 `_skillInstructions` 字段，改为 `List<SkillDescriptor> _activatedSkills`。

**去重保护**：同名 skill 不重复添加。

### 4.4 Compact 兼容

Compact 策略只操作 `Messages[1..]`（对话历史），`Messages[0]`（system prompt）保留不动。Compact 后 system message 内容可能过时（skills 列表包含已激活的等），但下轮发送前 `BuildSystemPrompt` 会重建覆盖，不影响实际发送。

## 5. Benefits

| 问题 | 解决方案 |
|---|---|
| System prompt 一次性构建，不反映激活状态 | 每轮重建 |
| `_skillInstructions` 累积膨胀 | 每轮重建，不会叠加 |
| 已激活 skill 同时出现在列表和 instructions | Builder 去重 |
| Compact 时 system prompt 不一致 | 保留 Messages[0]，发送前覆盖 |
| AgentLoop 持有太多种类依赖 | 只持有一个 `Func<Task<string>>` |

## 6. Files Changed

| File | Change |
|---|---|
| `SystemPromptBuilder.cs` | 新增 |
| `SystemPromptParams.cs` | 新增（或放入 Models） |
| `activated-skills.txt` | 新增模板 |
| `system-prompt-start.txt` | 新增模板（可选，将 base prompt 结构化） |
| `Agent.cs` | 替换 system prompt 构建逻辑，`_skillInstructions` → `_activatedSkills` |
| `AgentLoop.cs` | 替换追加逻辑为重建逻辑 |
