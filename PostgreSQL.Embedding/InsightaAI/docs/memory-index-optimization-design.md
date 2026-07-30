# Memory Index 轻量化与 SQLite FTS5 设计

## 1. 背景

当前 `GetMemoryIndexAsync()` 直接读取私有和团队 `MEMORY.md`，并将其完整注入 System Prompt。记忆条目会持续累积，因此每个用户 Turn 都携带大量与当前任务无关的信息。

现有文件 Provider 还以线性扫描实现搜索：用户输入按空格拆分，再检查词项是否出现在记忆的名称、描述、正文或标签中。中文输入通常没有空格，整句会成为单一查询词，召回质量与扩展性都不足。

现有 `MemoryEntry` 已有 `LastAccessedAt` 与 `AccessCount` 字段，文件 Provider 也能在按 ID 读取记忆时更新它们；但这些字段尚未定义统一访问语义，也没有参与“本 Turn 应注入哪些记忆”的决策。当前记忆几乎都是永久、且几乎都是每轮可见。

本设计将“永久保存”与“当前活跃”分离：永久记忆仍可检索、可修正、可删除；活跃记忆只是系统根据当前任务和真实使用行为计算出的短期投影。

## 2. 决策

第一阶段采用 **SQLite + FTS5**，不引入 embedding 或向量检索设计。

- SQLite 成为记忆的主存储和唯一事实源。
- FTS5 负责全文候选召回，SQLite 普通列负责用户、项目、权限等过滤。
- 活跃度（固定、访问频率、最近访问）只对召回候选重排，不能替代相关性。
- `IMemoryProvider` 保持抽象；旧 `FileMemoryProvider` 只作为迁移、导入导出或兼容实现，不再承担主检索路径。

这样先解决当前最实际的问题：从文件线性扫描和完整 Prompt 注入，过渡到有索引、可追踪、可预算的本地记忆系统；未来若有明确证据表明全文检索不足，再在 Provider 边界后讨论其他检索能力。

## 3. 目标与非目标

### 目标

- 不再把完整 `MEMORY.md` 作为每轮 System Prompt 的索引。
- 在一个用户 Turn 开始时，选出受 token 预算约束的活跃记忆快照，并复用于该 Turn 的所有 LLM Round。
- 对中英文自然语言、标签、错误码、文件路径提供本地全文检索。
- 将实际使用记忆的行为记录为可解释的访问证据，逐步识别活跃记忆。
- 对既有记忆平滑迁移；没有访问历史的条目仍能因文本相关性被首次召回。

### 非目标（第一版）

- 不物理删除“遗忘”的记忆，也不把低分记忆改为不可访问。
- 不让 Agent 自行声明某条记忆“活跃”或直接修改访问计数。
- 不在每个工具回合或每个 LLM Round 重复检索。
- 不根据工具输出自动重算任务主题。
- 不设计 embedding、向量列或向量数据库。

## 4. 数据模型与索引

SQLite 是唯一事实源。建议使用一张主表和一张 FTS5 虚拟表：

```sql
CREATE TABLE memory_entries (
    id                TEXT PRIMARY KEY,
    user_id           TEXT NOT NULL,
    scope             TEXT NOT NULL,
    project_id        TEXT,
    type              TEXT NOT NULL,
    name              TEXT NOT NULL,
    description       TEXT NOT NULL,
    content           TEXT NOT NULL,
    tags_json         TEXT NOT NULL,
    pinned            INTEGER NOT NULL DEFAULT 0,
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL,
    last_accessed_at  TEXT,
    access_count      INTEGER NOT NULL DEFAULT 0,
    last_confirmed_at TEXT
);

CREATE VIRTUAL TABLE memory_fts USING fts5(
    name,
    description,
    content,
    tags,
    content='memory_entries',
    content_rowid='rowid',
    tokenize='trigram'
);
```

`memory_fts` 的四列是面向检索的文本投影；`user_id`、`scope`、`project_id`、`type`、访问数据等仍由主表维护和过滤。写入、更新、删除记忆时，必须在同一事务中同步 FTS 索引（或通过 SQLite trigger 保证一致性）。

### 4.1 为什么使用 trigram

FTS5 的默认 `unicode61` tokenizer 将连续的 Unicode 字符视为一个 token。它适合以空格分词的语言，但不能自然处理中文句子的词边界。`trigram` tokenizer 将连续文本按三个 Unicode 字符的序列建立索引，支持一般子串匹配，因此无需在 Agent 内引入分词器。

它有明确边界：少于 3 个 Unicode 字符的全文查询不能命中 FTS trigram 索引。短项目名、标签、错误码或 ID 必须额外走主表精确匹配或前缀规则，不能只依赖 FTS。

## 5. 运行时流程

```text
收到用户输入
  │
  ├─ 提取可精确过滤的信息（user、project、scope）
  ├─ FTS5 MATCH + BM25 召回 Top 20 候选
  ├─ 合入 pinned 记忆与短精确词匹配候选
  ├─ 基于相关性、固定性和访问历史重排
  ├─ 在 MemoryTokenBudget 内构造 ActiveMemorySnapshot
  ├─ 将快照注入本 Turn 的动态 System Prompt
  └─ 对进入快照的条目记录一次强访问
       │
       ├─ LLM Round 1
       ├─ 工具调用 / LLM Round 2 / …（复用同一快照）
       └─ Agent 主动调用 search_memory 时，返回额外记忆并记录访问

下一个用户 Turn 才创建新的快照
```

这里的“Turn”是一次用户输入到最终 `AgentTurnEndEvent` 的完整 Agent 执行过程，不能等同于单个 LLM Round。工具调用导致的多轮推理必须复用同一份快照，避免重复检索、访问计数膨胀及上下文抖动。

## 6. 查询与排序

### 6.1 候选召回

以用户输入构造安全的 FTS5 `MATCH` 查询，调用 `bm25(memory_fts)` 得到候选排序；再 join `memory_entries` 并施加 `user_id`、`scope`、`project_id` 等过滤。第一版只使用用户的原始输入，不混入 Agent 的中间推理和工具输出，避免多轮推理中检索目标漂移。

名称、标签、项目名、错误码、路径和 ID 等可识别精确项应增加独立候选或加权，不依赖 trigram 对短文本的处理。

### 6.2 重排

候选先按 FTS5 BM25 相关性召回，再应用可解释的轻量修正：

```text
finalScore =
  lexicalRelevance
+ pinnedBoost
+ frequencyBoost
+ recencyBoost
```

- `lexicalRelevance`：由 FTS5 BM25 归一化而来，始终是主导项。
- `pinnedBoost`：少量硬约束/关键偏好优先，但仍受独立预算限制。
- `frequencyBoost`：对 `AccessCount` 使用对数缩放，避免高频旧记忆长期垄断。
- `recencyBoost`：由 `LastAccessedAt` 计算指数衰减，例如 `exp(-elapsedDays / stabilityDays)`。

高频但与当前输入无文本相关性的记忆不能仅凭 LRU 进入快照。

### 6.3 预算与降级

按排序依次加入快照，直到 `MemoryTokenBudget` 用尽。每条记忆在 Prompt 中使用长度受限的摘要（`Name`、`Description`、必要标签/来源），而非默认注入完整正文。无法放入快照的条目仍可通过 `search_memory` 找回。

FTS 查询失败或无命中时，降级为：固定记忆 + 记忆数量统计 + `search_memory` 使用提示。不得回退到全文 `MEMORY.md` 注入。

## 7. 访问语义

系统维护访问数据，而不是由 Agent 的主观判断维护。

| 行为 | 信号强度 | `AccessCount` / `LastAccessedAt` |
| --- | --- | --- |
| 条目被选入 `ActiveMemorySnapshot` 并注入 Prompt | 强 | 每个 Turn 最多更新一次 |
| `search_memory` 返回后，Agent 按 ID 读取完整条目 | 强 | 更新一次 |
| 用户明确确认、纠正或要求沿用该记忆 | 强 | 更新一次，并更新 `last_confirmed_at` |
| 仅进入初步候选、但未进入快照 | 弱 | 不更新 |
| Agent 保存、修改某条记忆 | 不是访问 | 只更新 `updated_at` |

应提供原子的 `TouchMemoryAsync(memoryId, turnId, reason)`；同一 `memoryId` 在同一 `turnId` 中只能产生一次强访问。这样一个多轮工具链不会人为放大 LRU 信号。

## 8. 接口边界

建议引入 `SqliteMemoryProvider`，保持 `IMemoryProvider` 的抽象不被 SQLite 细节污染：

- Provider：事务性保存、FTS 查询、精确查询和原子 `TouchMemoryAsync`。
- Manager：候选合并、重排、token 预算裁剪与快照构造。
- System Prompt Builder：只消费 `ActiveMemorySnapshot`，不知道 BM25、FTS 或持久化细节。
- `search_memory`：走 Manager；若 Agent 读取具体条目，再由 Manager 触发 `TouchMemoryAsync`。

运行时模型：

```csharp
public sealed record ActiveMemorySnapshot(
    string TurnId,
    IReadOnlyList<MemoryEntry> Entries,
    int EstimatedTokens);
```

`MEMORY.md` 可以在迁移期继续作为面向用户的可读导出，但不再是运行时注入的数据源，也不能成为第二份可编辑事实源。

## 9. 分阶段实施

### Phase 1：SQLite 主存储与全文召回

1. 定义 SQLite schema、迁移路径和 `SqliteMemoryProvider`。
2. 使用 `InsightaAI.Memory.Migrator` 将既有 Markdown 记忆一次性导入 SQLite；保留 Markdown 只读导出能力。
3. 用 FTS5 trigram + BM25 实现 `search_memory` 候选查询，并覆盖短查询的精确匹配分支。
4. 统一 `TouchMemoryAsync` 语义。
5. 在 Prompt 中仅注入数量统计和 `search_memory` 提示，先去除全文 `MEMORY.md` 注入。

### Phase 2：活跃快照

1. 引入 `ActiveMemorySnapshot` 和 `MemoryTokenBudget`。
2. 在用户 Turn 起点召回、重排并冻结快照。
3. 多轮工具调用复用快照；同 Turn 的强访问去重。
4. 补充 `pinned` 和用户确认时间。

### Phase 3：校准与维护

1. 记录召回、入选、因预算淘汰及实际读取的诊断数据。
2. 校准 BM25 权重、访问频率和衰减参数。
3. 提供冷记忆审查/归档界面，而非自动删除。

## 10. 历史 Markdown 迁移

`InsightaAI.Memory.Migrator` 是独立控制台程序，迁移私有记忆、团队记忆和私有用户画像。默认仅 dry-run；只有显式传入 `--apply` 才会写入 SQLite。迁移按既有记忆 ID upsert，不删除或修改源 Markdown 文件，因此可重复执行。

默认源目录为 `~/.insighta/memories`，默认目标为 `~/.insighta/memory/memory.db`。源、目标分离，便于验证迁移结果和回退；CLI 完成切换后只读取后者。

```powershell
# 预览默认 ~/.insighta/memories 目录的迁移结果
dotnet run --project src/InsightaAI.Memory.Migrator --

# 执行迁移
dotnet run --project src/InsightaAI.Memory.Migrator -- --apply

# 指定源目录与目标数据库
dotnet run --project src/InsightaAI.Memory.Migrator -- `
  --source D:\backup\memories `
  --database D:\data\insighta-memories.db `
  --apply
```

目录边界优先于旧 Markdown frontmatter：`private/{userId}` 中的记忆始终迁移为该用户的私有记忆，`team/{projectId}` 中的记忆始终迁移为该项目的团队记忆，避免旧字段错误地扩大可见范围。

## 11. 验收标准

- 多轮工具执行中，同一用户 Turn 只发生一次初始活跃记忆召回。
- 同一条记忆在同一 Turn 最多产生一次强访问计数。
- 中文相关短语可经 trigram FTS5 召回；短于 3 个字符的项目名/标签可经精确匹配召回。
- 无访问历史但与当前输入文本相关的记忆可被召回。
- 高频但与当前输入无关的记忆不能仅凭 LRU 进入快照。
- Prompt 中记忆占用不超过配置预算，`search_memory` 可找回未注入内容。
- FTS 失败或无命中时不会恢复全量 `MEMORY.md` 注入。

## 12. 参考资料

- [SQLite FTS5 Extension](https://www.sqlite.org/fts5.html)：内置 tokenizer、`unicode61` 的词项规则、`trigram` 的子串匹配能力及其少于 3 个字符时的限制。
