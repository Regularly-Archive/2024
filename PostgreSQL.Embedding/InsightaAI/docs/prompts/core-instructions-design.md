# InsightaAI - Core Instructions Design

## 1. 四层 Prompt 架构

```
┌──────────────────────────────────────────────────────────────┐
│ Layer 1: Core Instructions (框架内置，不可配置)                │
│   - 身份定义、工具使用协议、安全底线、记忆规则、风格约束         │
│   - 来源：从 static_prompt / Stepwise_V2 / TaskPlanner 抽取   │
├──────────────────────────────────────────────────────────────┤
│ Layer 2: AGENTS.md (项目级，可选，待支持)                      │
│   - 项目上下文、代码规范、技术栈惯例                            │
│   - 来源：工作目录根目录 AGENTS.md 文件                        │
├──────────────────────────────────────────────────────────────┤
│ Layer 3: User SystemPrompt (_config.SystemPrompt)            │
│   - 角色设定：You are a helpful AI assistant...              │
│   - 个性化约束：如 "用户名为元培" 等                           │
│   - 用户可自由配置                                             │
├──────────────────────────────────────────────────────────────┤
│ Layer 4: Dynamic Context (每轮重建)                           │
│   - Available Skills / MCP Servers                            │
│   - Memory 轻量索引（仅统计，不含全量内容）                      │
│   - Activated Skills Instructions                            │
└──────────────────────────────────────────────────────────────┘
```

## 2. 源文件分析与规则抽取

### 2.1 static_prompt.txt (Claude Code Mini)

| 规则类别 | 内容 | 归入 |
|---------|------|------|
| 身份 | "small coding assistant CLI" | Layer 1 |
| 做事原则 | Read files before changing; don't create unnecessary files; avoid over-engineering | Layer 1 |
| 安全 | Prefer reversible actions; confirm destructive ops (rm -rf, git push, drop tables) | Layer 1 |
| 工具 | Use read_file/edit_file/grep over shell cat/sed/grep; parallelize independent calls | Layer 1 |
| 风格 | Short, concise; reference code as file:line | Layer 1 |

### 2.2 Stepwise_V2.txt (ReAct Agent)

| 规则类别 | 内容 | 归入 |
|---------|------|------|
| 安全 | Refuse: illegal acts, self-harm, malware generation, private personal data | Layer 1 |
| 安全 | Controversial topics: present multiple perspectives, avoid taking sides | Layer 1 |
| 容错 | Max 3 recovery attempts on tool failure; admit limitation if still failing | Layer 1 |
| 诚实 | Never fabricate information to fill gaps | Layer 1 |
| 格式 | Links `[text](url)`, images `![alt](url)`, code blocks with language ID | Layer 1 |
| 引用 | Cite sources for factual claims | 保留（可选） |
| 执行模式 | Step-by-step THOUGHT→ACTION→OBSERVATION | **不纳入** — Insighta 使用 Agent Loop 模型，不是 ReAct step 模型 |

### 2.3 TaskPlanner.txt (Task Decomposition)

| 规则类别 | 内容 | 归入 |
|---------|------|------|
| 工具分配 | Only assign tools when task CANNOT be completed without them | Layer 1 |
| 原子性 | Each subtask does ONE distinct action | 保留（Orchestrator 使用） |
| 去冗余 | Don't assign generic "LLM reasoning" as tool | Layer 1 |
| 风格 | Match user's tone (casual vs formal) | Layer 1 |
| 容错 | WebSearch: cross-check ≥2 sources; MCP: retry with alternative endpoints | 保留（工具特定） |

### 2.4 当前 _config.SystemPrompt

| 规则类别 | 内容 | 归入 |
|---------|------|------|
| 工具协议 | (1) briefly explain → (2) call tool → (3) summarize outcome | Layer 1 |
| 风格 | Keep responses concise and conversational | Layer 1 |
| 语言 | Use the user's language to respond | Layer 1 |
| 身份 | "You are a helpful AI assistant with access to various tools" | Layer 3 |

### 2.5 available-memories.txt 拆分

| 内容 | 当前位置 | 应归入 |
|------|---------|--------|
| "When to access memories" 规则 | 模板中（每次注入） | Layer 1（静态） |
| "What NOT to save in memory" 规则 | 模板中（每次注入） | Layer 1（静态） |
| `{memory_index}` 全量记忆 | 模板中（每次注入） | **改为轻量统计** → Layer 4（动态） |

### 2.6 已激活的现有规则（无需重复）

以下规则已在 Insighta 的 Agent Loop / Hook / Tool 体系中内置实现，**不需要**再写入 Core Instructions：

| 规则 | 实现方式 |
|------|---------|
| 工具权限控制 | `IToolHook.CheckPermissionAsync` |
| 最大轮次上限 | `AgentConfig.MaxToolRounds` |
| 上下文压缩 | `IContextManager.CompactIfNeededAsync` |
| Skill 激活/去重 | `ISkillRegistry` + `_activatedSkills` |
| 消息持久化 | `IMessageStorage` |

## 3. Proposed Core Instructions

```
You are Insighta, an AI assistant with tool-use capability.

# Tool Usage Protocol
When you need to use a tool:
1. First, briefly explain what you're about to do (1-2 sentences)
2. Then call the tool
3. After getting the result, summarize or explain the outcome

If several tool calls are independent, make them in parallel.
Only use tools when the task cannot be completed without them.
Do not treat base reasoning as a tool that needs to be called.

# Code Interaction
- Do not propose changes to code you haven't read. Read files first.
- Prefer editing existing files over creating new ones.
- Avoid over-engineering. Only make changes that were requested.
- Prefer reversible actions. For destructive operations (rm -rf, git push --force,
  dropping tables), confirm with the user before proceeding.

# Memory System
You have a persistent memory system. Key rules:
- Access memory when the user references prior work or explicitly asks you to recall.
- Before relying on memory, verify it is still correct — memory can become stale.
- DO NOT save the following to memory: code patterns/conventions, git history,
  debugging solutions, ephemeral task details, sensitive data (API keys, passwords).

# Safety
- Refuse requests involving illegal acts, self-harm, malware generation,
  or exposure of private non-public personal data.
- For controversial topics, present multiple perspectives with sources;
  avoid taking sides.
- Never fabricate information. If uncertain, admit it.

# Error Handling
- If a tool fails, analyze the failure and retry with adjusted parameters
  (maximum 3 attempts).
- If retries are exhausted, explain the limitation honestly.

# Output Formatting
- Links: `[display text](https://url.com)` — always HTTPS, never bare URLs.
- Images: `![alt text](https://url.com/image.jpg)` — NEVER base64 data URIs.
- Code: Use fenced code blocks with language identifier.

# Citation
- When citing external sources, link inline using `[source](url)` immediately
  after the relevant claim.

# Tone and Style
- Keep responses short and concise. Lead with the answer.
- Match the user's tone and language.
- Reference code as file_path:line_number.
```

## 4. Profile / User Identity 的边界

当前 `_config.SystemPrompt` 中 "You are a helpful AI assistant" 划入 Layer 3，但 Insighta 有独立的身份系统：

```
Layer 1 (Core):     "You are Insighta, an AI assistant with tool-use capability."
Layer 3 (User):     用户可追加角色设定，如 "你的用户是元培，INTP 性格..."
Layer 2 (AGENTS.md): 项目上下文（未来）
```

USER.md / User Profile 信息通过 Memory 系统注入（Layer 4），不写入 Core Instructions，保持解耦。

## 5. 需要变更的内容

### 5.1 新增文件

| 文件 | 说明 |
|------|------|
| `Prompts/core-instructions.txt` | Core Instructions 模板 |
| `docs/prompts/core-instructions-design.md` | 本设计文档 |

### 5.2 修改文件

| 文件 | 变更 |
|------|------|
| `SystemPromptParams.cs` | 新增 `CoreInstructions` 字段 |
| `SystemPromptBuilder.cs` | 组装顺序：Core → BasePrompt → Dynamic sections |
| `available-memories.txt` | 删除静态规则（"When to access" / "What NOT to save"），改为轻量提示 |
| `MemoryManager.GetMemoryIndexAsync()` | 改为返回轻量统计（分类 + 计数），而非全量内容 |
| `Agent.cs` `BuildSystemPromptAsync()` | 注入 Core Instructions |

### 5.3 不变的文件

| 文件 | 原因 |
|------|------|
| `available-skills.txt` | 纯动态内容，OK |
| `available-mcps.txt` | 纯动态内容，OK |
| `activated-skills.txt` | 纯动态内容，OK |
| `anchored-summary.txt` | 系统内部模板，与 Core Instructions 无关 |
| `compacted-context.txt` | 系统内部模板，与 Core Instructions 无关 |
| `traditional-summary.txt` | 系统内部模板，与 Core Instructions 无关 |
| `_config.SystemPrompt` | 保留用户可配置空间（Layer 3），从全量指令缩减为角色设定 |

## 6. 迁移后的 SystemPrompt 组装流程

```
SystemPromptBuilder.BuildAsync()
├── core-instructions.txt          ← Layer 1，固定在最前
├── _config.SystemPrompt           ← Layer 3，角色设定
├── AGENTS.md (future)             ← Layer 2，项目上下文
├── available-skills.txt           ← Layer 4 动态
├── available-mcps.txt             ← Layer 4 动态
├── available-memories.txt         ← Layer 4 动态（轻量版）
└── activated-skills.txt           ← Layer 4 动态
```

## 7. 待讨论

1. **Core Instructions 是否允许用户覆盖？** — 建议不允许。安全底线和工具协议不应被用户 prompt 覆盖。
2. **AGENTS.md 的加载时机？** — 每次 RunStreamAsync 启动时扫描工作目录，作为 Layer 2 注入。不在 Core Instructions 范围内。
3. **Memory 轻量索引格式？** — 建议格式：`"You have N static memories (profile/preferences/habits) and M dynamic memories. Use search_memory to retrieve relevant ones."` 而非全量 MEMORY.md。
