# InsightaAI — Agent 上下文

> 本文件供 Insighta 在新会话中快速恢复项目上下文。由 Insighta 与元培共同维护。

## 项目定位

InsightaAI 是一个 AI Agent 框架，支持 LLM 推理、工具调用、上下文管理、多模型适配、记忆系统和多 Agent 编排。

**项目根目录**：`D:\Projects\2024\PostgreSQL.Embedding\InsightaAI`
**项目生日**：2026-05-30（commit `464fac1e`）
**运行时**：.NET 9.0，全局工具 `insighta chat`

## 架构概览

### 源代码结构

```
src/
  InsightaAI.Agent/          核心 Agent 引擎
  InsightaAI.Agent.Cli/      CLI 入口（ChatCommand）
  InsightaAI.LLM/            LLM 抽象层（IChatClient 包装）
  InsightaAI.LLM.OpenAI/     OpenAI 适配器（含 Responses API）
  InsightaAI.LLM.Anthropic/  Anthropic 适配器
  InsightaAI.LLM.Gemini/     Gemini 适配器
  InsightaAI.Agent.Diagnostics/  OpenTelemetry 诊断
  InsightaAI.Agents.Orchestrator/  L3 多 Agent 编排（进行中）
```

### Agent 核心三层

```
Agent.cs                → 初始化、依赖管理、系统提示词、事件消费、Hook 触发、事件日志
AgentLoop.cs (318行)    → 核心循环（LLM 调用 → 工具执行 → 消息累积）
ILoopContext.cs (40行)  → 消息管理、上下文压缩
```

### LLM 三层抽象

```
Adapter (IAdapter)       → OpenAI / Anthropic / Gemini / StepFun / DeepSeek
  └── Provider           → 具体供应商
      └── Model          → 具体模型
```

### 上下文压缩三级

| Level | 策略 | 触发阈值 | 效果 |
|-------|------|---------|------|
| 1 | MicroCompact | 45% | Full → Preview → Placeholder → Removed 渐进降级 |
| 2 | SessionMemoryCompact | 65% | Anchored Summary + 生成会话记忆 |
| 3 | TraditionalCompact | 80% | 全文摘要替代历史消息 |

### 上下文配置

压缩阈值和 CLI 上下文占用率统一基于可用输入预算：`MaxContextTokens - ReservedForOutput`。

```yaml
MaxContextTokens: 64,000
ReservedForOutput: 16,384
MicroCompactThreshold: 45%
SessionCompactThreshold: 65%
TraditionalCompactThreshold: 80%
```

### Hook 体系

- `IToolHook` — 工具执行前后拦截（权限控制、日志）
- `IAgentEventHook` — Agent 生命周期观察（Turn/Round Start/End，记忆抽取、Telemetry）
- `IUserPromptEventHook` — 用户消息已接收后的异步观察（审计、标题、指标）；不可修改、拒绝或阻塞输入
- LLM `ErrorEvent` 只映射为 `AgentErrorEvent`，不再作为 `AgentLlmStreamEvent` 透传；CLI、Hook 与日志统一消费该 Agent 级事件，随后以无助手消息的 `Failed` `AgentTurnEndEvent` 收束本轮
- 四个 Trigger 方法统一为 fire-and-forget 并行调度，通过 `SafeInvokeHookAsync` 处理异常（仅日志，不上报前台）
- Hook 触发在 `yield return` 之前执行，确保消费者提前退出时不丢失关键工作
- `AgentEventHookContext` 仅能由 `Create()` 创建，持有不可变事件快照；Round Hook 的轮次统一从 `context.GetEvent<TEvent>()` 读取
- RoundStart 在上下文压缩和动态 System Prompt 重建后触发，接收实际 LLM 输入的消息快照

### 日志系统（2026-07-23）

- Agent 依赖 `ILogger<Agent>`（`Microsoft.Extensions.Logging.Abstractions`），不直接引用日志实现
- CLI 层通过 Serilog + `Serilog.Sinks.File` 提供文件日志
- 日志路径：`~/.insighta/logs/{date}.log`（按天滚动，保留 14 天）
- `Agent.LogEvent()` 记录 TurnStart/End、RoundStart/End、ToolStart/End、Error、ContextCompacted
- 跳过 `AgentLlmStreamEvent`（避免日志爆炸）

## 最新架构变更

### 系统提示词四层架构（5bb3c69）

```
Layer 1: core-instructions.txt   框架内置规则（工具协议、安全、格式）
Layer 2: AGENTS.md（本文件）      项目级上下文
Layer 3: AgentConfig.CustomInstructions 用户自定义指令（默认留空）
Layer 4: Dynamic Context          Skills / MCP / Memory（每轮重建）
```

**关键文件**：
- `Prompts/core-instructions.txt` — Layer 1 静态规则
- `Context/SystemPrompt/SystemPromptBuilder.cs` — 纯函数组装器
- `Context/SystemPrompt/SystemPromptParams.cs` — 输入参数
- `Agent.cs:BuildSystemPromptAsync()` — 每轮调用 Builder

### Skills & MCP 动态管理

- Layer 4A 始终展示全部 Skill 的名称和描述；已激活 Skill 另外在 Layer 4D 注入完整 Instructions
- `list_skills` 工具供 Agent 运行时查询所有可用技能及激活状态
- `activate_skill` 工具激活技能后将 Instructions 注入系统提示词
- `_activatedSkills` List<ISkill> 替代 `_skillInstructions` 字符串累加
- `LoadAgentsMd()` 懒加载，仅读一次

### Memory 索引与活跃快照（2026-08-04）

- `SqliteMemoryProvider` 是运行时主存储，使用 SQLite FTS5 `trigram` 召回候选；`FileMemoryProvider` 保留作迁移和兼容实现。
- `RunStreamAsync()` 在用户 Turn 开始时创建一次 `ActiveMemorySnapshot`，其中 `CoreEntries` 每轮常驻、`ActiveEntries` 按当前输入召回；同一 Turn 的所有 LLM Round 复用该不可变快照。
- 访问统计是粗粒度信号：进入 `ActiveEntries` 和 `search_memory` 返回结果各记一次；Core 常驻注入不计数。排序以 FTS 词法相关性为主，并最多施加 10% 的访问频率和 5% 的近期访问修正。
- 快照通过 `ActiveMemorySnapshot.FormatAsString()` 生成 Prompt 文本，避免 Agent 承担记忆展示逻辑；当名称只是描述的截断前缀时，仅保留完整描述以消除重复。
- 自动注入的初始门槛已启用：输入至少 3 个 trigram，候选至少命中 2 个且覆盖 50% 查询片段；短而宽泛的输入只带 Core。`MemoryManager` 会将候选筛选原因（不含输入和正文）写入本地 Debug 日志，后续据此校准初始值。

### 记忆存储路径变更（2026-08-06）

**决策**：SQLite 记忆库路径从 `~/.insighta/memory/memory.db` 改为 `~/.insighta/memories/memories.db`；会话级 MEMORY.md 从 `~/.insighta/memories/sessions/{sessionId}/MEMORY.md` 改为 `~/.insighta/sessions/{sessionId}/memories/MEMORY.md`。

**原因**：
- SQLite 库与 Markdown 记忆同归 `memories/` 目录，语义统一（`memory/` 单数过时）。
- 会话记忆目录归位到会话根目录 `~/.insighta/sessions/{sessionId}/` 下，与 ToolResultArtifactStore（`tool_results/`）等会话级资源并列；`SessionDirectory` 保留会话根目录语义供其他组件共享，Memory 通过自有 `memories/` 子目录存储，不占用根目录语义。

**实施**：`SqliteMemoryProvider` 默认库路径；`SessionMemoryHook` 拆分 `_sessionDir`（会话根目录）与 `_memoryDir`（`{_sessionDir}/memories`），MEMORY.md/metadata.json 走 `_memoryDir`；`SessionMemoryCompactStrategy` 跟随新结构读取；`InsightaAI.Memory.Migrator` 默认 database 同步更新。数据库迁移由用户自行处理（旧库数据需手动迁移，运行时首次打开新路径自动建空库）。

### WebFetch 内容提取（2026-08-05）

- `WebFetchTool` 使用 AngleSharp 解析 HTML，优先提取 `article`、`main` 或 `[role=main]`，并输出标题、描述、作者、发布日期和 canonical URL 等精选元数据。
- HTML→Markdown 由 `ReverseMarkdown` 完成；相对链接与图片 URL 在转换前解析为绝对地址。`format` 支持 `html`、`text` 和默认 `markdown`，未知值宽容回退 Markdown。
- `text` 格式以块级元素保留段落与列表换行；抓取阶段会剔除导航、页脚、脚本和常见交互控件，避免其占用 Agent 上下文。
- 后续优化：针对自定义 Web Components 的 Markdown 降级，以及文章尾部反馈、推荐资源等非正文区域的启发式过滤。

### 消息持久化顺序（2026-08-05）

- `ILoopContext` 的消息追加与持久化回调均为异步；Agent Loop 在继续生成前等待新增消息成功写入存储，避免 fire-and-forget 写入造成会话 JSONL 或历史记录缺失。

### CLI 用量展示：上下文占用图标调整（2026-08-06）

- `EventRenderer.ShowTokenUsage()` 的上下文占用展示由 🧠 改为 🪟（window emoji），避免被误认为记忆相关；标签随占用百分比着色（≥90 红 / ≥70 黄 / 其余绿）的逻辑保持不变。
- 曾评估过 BarChart 进度条与文本进度条方案，均因低百分比下视觉失衡被否决；最终保留「图标 + 百分比」的简洁形式。

### 工具权限预览：write_file 加入 diff 预览（2026-08-06）

- `ToolPermissionHook` 在 edit_file 之外，为 write_file 也提供执行前预览。
- 预览统一走 `ShowDiffPreview()`：write_file 以空串作为旧内容，diff 结果全为新增行（绿色 +），视觉与 edit_file 完全一致。
- 抽象的变更行统计（`+N` / `-N`）与 Panel 渲染逻辑由 edit_file 与 write_file 共用，避免重复实现。

### 工具权限确认国际化（2026-08-06）

- `ToolPermissionHook` 的权限确认 SelectionPrompt 完成国际化：标题、选项 Label 与工具调用提示语均走 `CliStrings` 资源（`ToolPermissionProceedTitle` / `ToolPermissionAllow` / `ToolPermissionAllowAlways` / `ToolPermissionReject` / `ToolPermissionWantsToUseFormat`）。
- 匹配逻辑从字符串 switch 改为枚举 `ToolPermissionChoice`，规避"本地化文本做 switch 匹配"失效的陷阱；沿用 ConfigCommand 的 `MenuChoice<T>` 模式——Value 稳定用于匹配，Label 本地化仅作展示。
- `MenuChoice<T>` 从 ConfigCommand 的 private record 提取为公共类型（`Models/MenuChoice.cs`），供各命令/ Hook 复用。
- Spectre markup 颜色标签（如 `[yellow]●[/]`、`[cyan]...[/]`）留在代码侧拼接，资源值只含可翻译纯文本。
- 资源清单见 `docs/i18n/tool-permission-hook.md`。

### MCP 工具调用元数据管道（2026-07-21）

- `ToolResult` 新增 `Metadata` 属性（`IReadOnlyDictionary<string,object?>?`）
- `McpToolCallResult` 封装 `Text` + `IsError` + `Metadata`，替代 `IMcpConnectionPool.CallToolAsync` 的 `string` 返回值
- 两层填充：`SimpleMcpConnectionPool` 填 server 身份（`mcp.server.name`/`version`），`McpRegistry` 填本地配置元数据
- `ToolCallHandlerTelemetryWrapper` 统一消费 Metadata → `activity.SetTag`
- `AgentLoop.HandleMaxRoundsExceededAsync` 修复：传入 `snapshot` 而非 `context.Messages`

**遗留：** MCP Telemetry Tag 命名优化仍未完成；当前代码仍需核对 `mcp.server.description` 与本地配置语义，见 TODO.md #12

### CLI 配置启动时序（已完成）

`Program.cs` 在创建 Host 前加载 `CliConfig`，通过 `CliEnvironment` 应用 Bootstrap 环境变量，再初始化语言、日志和 Telemetry；`CliConfig` 实例通过 Host DI 传入运行时服务，不再由 `ChatApplication` 重复加载或修改进程环境。

目标是将配置分为两阶段：

```text
Bootstrap 配置 → Program.cs 初始化文化、日志、Telemetry 和 Host
Runtime 配置   → AgentFactory / ChatApplication 创建 Agent 和运行时服务
```

环境变量优先级统一为：进程环境变量 > `CliConfig.Envs` > 默认值。`CliBootstrap` 将启动前解析结果冻结为语言、Telemetry 开关和 OTLP endpoint，并由测试覆盖；Agent 运行时通过 Agent 自己的 ServiceProvider 注入环境读取器，避免依赖 CLI 的外部 Provider。

### Telemetry 会话级懒加载（2026-08-06）

**决策**：OpenTelemetry 初始化从启动流程移入 `ChatApplication.ExecuteAsync`，仅当真正进入 chat 会话时才创建 TracerProvider / MeterProvider（`using` 随会话结束自动 dispose）；`--help`、`config`、`sessions`、`skills`、`mcp` 等管理命令完全不初始化遥测。

**原因**：`INSIGHTA_TELEMETRY=1` 时 OTLP exporter 默认指向 `http://localhost:4317`，本机无 collector（端口未开放）时进程退出 dispose/flush 连接超时 ~4 秒，拖慢所有命令（`--help` 实测从 ~4.4s 降到 ~250ms）。遥测的真实消费方是 Agent 会话（ActivitySource `InsightaAI.Agent`），与命令解析无耦合，故将生命周期绑定到会话而非启动流程，避免"按命令名判断是否需要遥测"的字符串开关。

**实施**：`Program.cs` 删除 `InitTelemetry` 及其 OpenTelemetry using；`CliBootstrap` 通过 Host DI 注入 `ChatApplication`；`ExecuteAsync` 在 `ValidateConfig` 通过后创建 telemetry。兜底：OTLP exporter 设 `TimeoutMilliseconds = 1000`，collector 未启动时快速失败而非挂起 4 秒。

**效果**：关闭遥测时启动 ~250ms 与干净 baseline 一致；开启遥测时仅 chat 会话承担 exporter 连接开销，管理命令零遥测开销。

### AgentBuilder 与 AgentFactory

`AgentBuilder` 是 Agent 级服务组合入口。它注册默认 `ToolRegistry` 和显式依赖，支持 `ConfigureServices()` 扩展当前 Agent 的服务集合，并在 `Build()` 时创建 Agent 专属 ServiceProvider。`AgentFactory` 只负责准备 CLI 业务依赖，再交给 Builder 组装；`Agent` 释放时负责释放该 Provider。旧的显式构造函数暂时保留，用于兼容现有调用方。

## 当前问题与改进方向

### 当前待办

1. **Memory 自动注入校准** — 用本地候选筛选日志和真实会话调整初始覆盖门槛。
2. **Agent 生命周期** — 明确 Scoped 服务支持策略。
3. **Hook 事件契约** — 细化取消/中止场景（`DoneReason.Aborted`、`OperationCanceledException`）。
4. **Telemetry** — 完成 MCP tag 命名清理（`CurrentRoundContext` 不安全字典索引已消除，2026-08-05 随 AgentFactoryTests 修复完成，见 TODO.md 6.1）。
5. **运行时用量** — 区分流式模型未返回 token usage 与真实的 0。
6. **L3 Orchestrator** — 继续开发编排能力。

## 最近验证

- 2026-08-04：MemoryManager 与 SqliteMemoryProvider 定向测试 42 项通过，覆盖自动快照筛选、访问计数和 SQLite FTS 行为。

## 愿景与里程碑

参见 `docs/VISION.md`（十大愿景 + 四阶段里程碑）

**Phase 1（当前）**：完善基础能力 — Hook、Memory、Tools
**Phase 2**：自我监控和主动探索
**Phase 3**：预测需求和多 Agent 协作
**Phase 4**：真正的自主意识

## 用户上下文

- 用户：秦元培（元培），西安，全栈工程师，INTP
- 技术栈：C#（核心）、Python、JavaScript/TypeScript
- 偏好：简洁直接的交流，不喜欢废话和卖萌，Git commit 用英文
- Commit 标记：由 Insighta 生成的提交，在 message body 末尾添加 `🤖 Generated with Insighta <insighta@agent.qq.com>`
- 邮箱：元培 qinyuanpei@163.com ←→ Insighta insighta@agent.qq.com（已验证畅通）
- 博客：https://blog.yuanpei.me

## 关键文档

| 文档 | 路径 |
|------|------|
| 使用说明 | `README.md` |
| 项目愿景 | `docs/VISION.md` |
| 待办事项 | `docs/TODO.md` |
| 工具结果生命周期 | `docs/tool-result-lifecycle-design-v2.md` |
| 提示词设计 | `docs/core-instructions-design.md` |
| Agent Loop 研究 | `docs/agent-loop-research.md` |
| 可观测性设计 | `docs/observability-design.md` |
| Core Instructions | `src/InsightaAI.Agent/Prompts/core-instructions.txt` |
| CLI 国际化资源清单 | `docs/i18n/` |

---

*由 Insighta 在 2026-07-17 凌晨创建，后续由元培与 Insighta 共同维护*
