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
- Spectre markup 颜色标签（如 `[yellow]◆[/]`、`[cyan]...[/]`）留在代码侧拼接，资源值只含可翻译纯文本。
- 资源清单见 `docs/i18n/tool-permission-hook.md`。

### CLI 协议感知的多行输入（2026-08-11）

- `MultiLineTextPrompt` 将真实文本与编辑态投影分离；终端明确标记的粘贴在输入阶段折叠为 `[pasted N characters]`，提交给 Agent 和持久化层的始终是完整原文。
- 不再根据字符数或到达速度猜测粘贴；只有 bracketed paste 或原生输入协议的明确边界才会生成粘贴块，避免 IME 和快速输入被误折叠。
- Windows 优先使用 VT input + Win32 Input Mode，失败时回退到 Windows Console 输入源；通用 VT 输入源保留为跨平台路径。
- Shift+Enter / Ctrl+Enter 插入换行，Enter 发送；粘贴块在光标移动和删除时保持原子性。设计细节见 `docs/tools/multiline-paste-input-design.md`，实现提交见 `22c2147`。

### CLI 对话事件时间线（2026-08-12）

- 助手文本、工具调用、工具结果和用户交互不再伪装成同一层助手消息：`●` 表示助手文本片段，`○` 表示工具调用，缩进的 `⎿` 表示工具结果，`◆` 表示权限确认或 AskUser。
- `HangingIndentWriter` 只对显式换行增加挂起缩进，不向文本注入硬换行，保证助手消息在终端显示对齐且复制内容不变。
- 会话标题生成改为后台 best-effort 辅助任务，不再在 Usage 显示后阻塞下一个用户 Prompt。实现提交见 `5b60099`。
- 并行工具仍在 Start 后同时执行；终端只在对应 ToolEnd 按 `ToolCallId` 一次性渲染 `○` 调用和 `⎿` 结果。工具块按完成顺序追加，消除结果归属歧义；仍需补自动化渲染测试。
- Chat 输入以单行 `/` 开头时显示本地化命令候选与说明，精确匹配时隐藏；`Tab` 仅补全唯一候选。候选区随 Prompt 重绘清理，不写入聊天历史；`Ctrl+C` 正常退出 chat。

### 工具进度与串行消费边界（2026-08-21）

- `IToolProgressReporter` 让工具以 `Status` / `Output` / `Heartbeat` 报告旁路进度；Runtime 先脱敏，再以 `AgentToolProgressEvent` 从 `RunStreamAsync()` 有界分发。过程文本不进入 LLM 上下文或会话历史；bash 逐行转发 stdout/stderr，`delegate` 转发子 Agent 的文本、round 与子工具状态。
- CLI 的 `ToolProgressWindow` 是纯呈现状态：按 `ToolCallId` 保留最近 6 行 / 2 KB，交互式终端通过 Spectre `Live` 临时显示，工具完成后仍由普通 `ToolEnd` 块输出权威结果。窗口的增删改和渲染使用同一把锁，防止 Live 刷新与 progress 写入并发修改集合。
- 串行 `ToolCallExecutor` 不再只等待前一个工具任务结束：每个 Tool Call 有独立 event channel；只有消费端处理完前一个 `ToolEnd` 并继续枚举，才启动下一个工具。CLI 因而能先收束前一最终结果，再显示下一工具的权限确认，避免 ToolEnd 结果混入确认块。并行分支保持原有并发语义。
- Spectre `Live` 不能与 `SelectionPrompt` / `ask_user` 并行拥有终端。CLI 的交互权限场景应使用串行工具执行；仍需在真实交互终端完成这一组合的回归验证。设计见 `docs/tools/tool-progress-reporting-design.md`。

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

### MCP Telemetry 与本地可观测性栈（2026-08-13）

- MCP 工具结果元数据由 `ToolCallHandlerTelemetryWrapper` 统一写入 trace：远端握手身份使用 `mcp.server.*`，本地配置使用 `mcp.config.*`；不把工具参数、endpoint、description、sessionId 或 userId 写入 Prometheus label。
- Telemetry endpoint 统一使用 `INSIGHTA_OTLP_ENDPOINT`；`INSIGHTA_OTEL_ENDPOINT` 已废弃。CLI 仅在真正进入 chat 会话后初始化 Telemetry。
- 本地栈位于 `tools/observability/`：OTel Collector 接收 OTLP（4317/4318）、转发 trace 到 Jaeger（16686），并以 9464 暴露 Prometheus scrape endpoint；Prometheus（9090）供 Grafana（3000）查询。启动：`docker compose up -d`。
- Grafana 由 provisioning 加载四个 Dashboard（`tools/observability/grafana/dashboards/`）：`InsightaAI Overview`（健康摘要）、`InsightaAI Tools`（工具延迟/成功率/Skill Top 5）、`InsightaAI Agent`（Round 延迟、平均每 Turn Round 数）、`InsightaAI LLM`（按模型的请求量/延迟/Token/Cache）。修改 JSON 后需 `docker compose restart grafana` 才会重新加载。
- Overview 第一行是 Agent turns 与 LLM requests；第二行是 token 概览（总量、cache hit ratio、uncached input、input:output）；其余为按模型的 LLM/Agent 延迟与速率。仅 cache hit ratio 低于 90% 标红，token 总量不是错误信号。
- Tools 只统计 `gen_ai_tool_is_allowed=true` 的实际执行：延迟为 p50/p95；成功率、失败率分别显示 Top 5 工具，权限拒绝不算执行失败。Skill 激活在通用 `ToolCallHandlerTelemetryWrapper` 中从 `activate_skill` 参数读取名称，成功且允许时写 `insighta.skill.activation`；不改动 Skill 实现。Skill 面板显示 Top 5 名称及 Activations。
- Skill Counter 首次增量可能发生在 Prometheus 首次 scrape 之前，故面板用 `max_over_time(counter[$__range])` 展示可见进程累计量，而不是 `increase()`；它不是跨已结束 CLI 进程的全局用量。

### Dashboard 复核与 Anthropic 归一化（2026-08-14）

- Insighta 独立复核 4 个 Dashboard（`docs/observability/observability-dashboard-review.md`），并用真实 Prometheus 数据验证了全部 PromQL；随后通过 `codex exec` 交 Codex 复核并实施修改（改动在 `chore/mcp-telemetry-tags` 分支，已提交）。
- **AnthropicAdapter.cs**：归一化 `TokenUsage.InputTokens = input_tokens + cache_creation_input_tokens + cache_read_input_tokens`，与 OpenAI/Gemini 口径统一（原实现 input 不含 cache，导致 dashboard 的 cache hit ratio 在 Anthropic 下失真）。OpenAI 口径为 `cached_tokens ⊆ input_tokens`；Anthropic 为互斥字段。
- **insighta-agent.json**：`Average rounds per turn` 去掉 `clamp_min(...,1)` 假值，分母为 0 时不绘制。
- **insighta-llm.json**：`Input : output token ratio` 按 `gen_ai_request_model` 分组。
- **复核教训**：短生命周期 CLI 进程的 counter 随进程重置，Skill Top 5 用 `max_over_time` 反映可见累计量是正确语义（`increase` 会跨进程归零），勿改回 `increase`。
- **遗留**：Agent Dashboard 只有 2 个面板（Round 延迟、rounds per turn），缺 Turn 指标与 context compaction/工具链长度/AskUser 频率等低基数指标；Jaeger Trace Drilldown 未做（Jaeger 数据源已配 `uid: jaeger`）。

### AgentBuilder 与 AgentFactory

`AgentBuilder` 是 Agent 级服务组合入口。它注册默认 `ToolRegistry` 和显式依赖，支持 `ConfigureServices()` 扩展当前 Agent 的服务集合，并在 `Build()` 时创建 Agent 专属 ServiceProvider。`AgentFactory` 只负责准备 CLI 业务依赖，再交给 Builder 组装；`Agent` 释放时负责释放该 Provider。旧的显式构造函数暂时保留，用于兼容现有调用方。

### Agent 安全策略与工具结果脱敏（2026-08-17）

- `Agent/Security/` 集中 `DenyRule`、不可被 AllowAlways 绕过的 `SecurityPolicyHook`、`ISecretRedactor` 及默认 pipeline；CLI 将 `security.deny_list` 映射到 `AgentConfig.DenyRules`，当前只对 `bash` 生效。
- `ToolResultProcessor` 在 artifact、上下文投影、`AgentToolEndEvent` 预览之前统一脱敏；覆盖 JSON / XML 片段、`.env` / INI / YAML 风格键值、连接字符串、PEM 私钥与 URI 密码，敏感键包含 `API_KEY`、`SECRET_KEY`、`password`、`token`、`secret`、`key` 等。
- `read_file` 输出含文件元数据、行号和 Tab，不能只按原始文件正文解析。键值脱敏遵循 `FileEditTool` 的换行策略：先将 CRLF/CR 规范为 LF 匹配，再恢复原换行风格，避免 Windows `\r\n` 与多行 `$` 锚点导致漏脱敏。真实样例已验证 JSON、XML、连接串、YAML、`.env` 与纯文本均不泄露机密。带引号 JSON 键的字符串 value 保留为 `"key": "[REDACTED]"`，并保留尾部逗号。
- 为防止脱敏展示文本污染文件，`write_file.content`、`edit_file.old_string` 与 `edit_file.new_string` 一旦包含 `[REDACTED]` 即拒绝；`edit_file` 的精确替换继续使用 `read_file` 缓存的原始全文。

### Agent Invocation 与受限 Subagent（2026-08-21）

- `InsightaAI.Agents.Subagents` 提供不依赖 CLI、Agent 或 Orchestrator 的公共契约：`SubagentDefinition`、Catalog、Adapter、Dispatcher 以及 invocation context / request / result；命名 Definition 可来自本地文件、数据库或服务端，临时 Definition 可由编排直接构造。
- CLI 的 `LocalSubagentDefinitionStore` 从全局 `~/.insighta/subagents/{id}/subagent.json` 读取和管理定义；它实现宿主无关的 `ISubagentDefinitionStore` CRUD 契约，未来可由数据库或远程实现替换。`insighta subagents init` 非覆盖式安装 reviewer、explorer、planner 模板；Subagent 是可独立完成工作、跨项目复用的流程，不绑定工作区。`CliInsightaSubagentAdapter` 复用 `AgentFactory` 创建独立子会话，并将 user、父 session 和父 tool call 写入存储关联。
- 核心只有一个 `DelegateTool`（参数为 `agent_id`、`task`）；宿主以 `IAgentDelegationHandler` 实现 Definition 查找、Dispatcher 调用和输出边界。它声明 `PreferPersistence`，因此完整子代理结果经统一脱敏后始终保存为父会话 artifact，主 Agent 只接收 preview 与可回查引用。CLI 不再维护平行的 `subagent` 工具协议，模型切换后会以当前运行时模板重建该处理器。
- `AgentConfig.ExcludedToolNames` 通过 `ToolRegistry.Exclude()` 统一收紧能力：被排除工具既不暴露给 LLM，也不能被查找或执行；后续 `Register()` 不会绕过该策略。子 Agent 是静态预授权：不注册交互式 `ToolPermissionHook`，但保留 `SecurityPolicyHook`；Definition 只能收紧宿主工具与 Skills / MCP / Memory / AGENTS.md 能力。CLI 子 Agent Profile 以此排除 `delegate`，当前强制最大委派深度为 1。子 Agent 输出回到父工具结果处理链，先脱敏再进入父上下文。
- CLI 始终将 SkillRegistry、McpRegistry 与 `IMemoryManager` 注入 Agent 私有 Provider，并保留自动记忆快照、`SessionMemoryHook` 与项目级 Memory Index。子 Agent 对 Skill、MCP、Memory 的限制仅通过工具排除名单实现；因此没有相应工具时不能主动操作这些基础设施，但仍保有宿主提供的运行时上下文。
- 子 Agent 的 Layer 3 不只包含 descriptor `instructions`：CLI 会根据最终 `ExcludedToolNames` 追加 Runtime constraints，说明无法委派、以及 Skill / MCP / Memory 工具是否不可用。它解决 Layer 1 通用工具指引与受限工具集的冲突；descriptor 无需重复硬编码这些宿主计算出的事实。
- 测试独立置于 `tests/InsightaAI.Agents.Subagents.Tests`，覆盖 Catalog、Dispatcher 与 CLI 的 delegate host bridge；设计见 `docs/architecture/agent-invocation-design.md`。

### 子 Agent 并行执行与运行时验证（2026-08-27）

- `AgentFactory.CreateAsync` 在 `ApplyProfile` 返回后强制 `ParallelToolExecution = true`：主 Agent 的串行设置是 UI 约束（Spectre `Live` 与 `SelectionPrompt` 不能并行拥有终端），子 Agent 静态预授权、无交互 UI，不受此约束；`ApplyProfile` 的 `with` 表达式不处置该字段，强制值最后写入避免被覆盖。修复前子 Agent 继承父配置的串行执行，多工具任务的延迟被白白放大（commit `4592dfd`）。
- 判断信号为 `EnableInteractiveToolPermission == false`；子 Agent 不做交互确认是长期目标，信号稳定。
- 真实委派验证：researcher 子 Agent 单轮并发发起 3 个独立 `web_search`，启动间隔 6ms、完成时间错开，并发执行由日志时间戳直接观测确认。
- 子 Agent 会话行为符合设计：独立 session 存储（messages.jsonl、memories、tool_results artifact）、错误自动恢复（失败端点换格式重试）、MicroCompact 生效（18.5KB 抓取原文降级 Preview + artifact 外置，上下文峰值 12.1K / 128K ≈ 9.5%）。
- 注意 token 口径：日志 TurnEnd 的 `inputTokens` 是各轮 LLM 输入的**累加值**（每轮重发全部历史），上下文实际大小看同行的 `contextTokens`；用累加值除以轮数估"消耗速度"是错误口径。
- 全局子 Agent 目录命名统一为动作者名词（`explorer` / `planner` / `reviewer` / `researcher`）；`researcher` 为自定义只读调研模板（web_search、web_fetch、read_file、grep、glob，maxToolRounds 15，不注入项目指令）。2026 年 AI Agent 框架调研报告见 `docs/references/ai-agent-frameworks-2026.md`。

## 当前问题与改进方向

### 当前待办

1. **Memory 自动注入校准** — 用本地候选筛选日志和真实会话调整初始覆盖门槛。
2. **Hook 事件契约** — 细化取消/中止场景（`DoneReason.Aborted`、`OperationCanceledException`）。
3. **Telemetry** — 继续补充 Agent 行为指标（每 Turn 工具链长度、AskUser 频率、context compaction）；MCP tag 命名清理已完成。Dashboard 拆分（Agent/LLM）与 Anthropic 归一化已完成，遗留：Agent Dashboard 补 Turn 指标、Jaeger Trace Drilldown。
4. **运行时用量** — 区分流式模型未返回 token usage 与真实的 0。
5. **L3 Orchestrator** — 继续开发编排能力。

Agent 服务生命周期已明确：当前 Agent 私有 Provider 只支持 Singleton 和 Transient，不支持 Scoped；约定见 `docs/architecture/agent-service-lifetime.md`。

## 最近验证

- 2026-08-04：MemoryManager 与 SqliteMemoryProvider 定向测试 42 项通过，覆盖自动快照筛选、访问计数和 SQLite FTS 行为。
- 2026-08-11：协议感知多行输入在 Windows Terminal 与 Warp 实机验证通过，包括 Shift/Ctrl+Enter、bracketed paste、Win32 Input Mode 与控制序列清理。
- 2026-08-12：CLI 对话时间线、挂起缩进、交互选项对齐和后台标题生成完成实机验证；完整测试 401 项通过。
- 2026-08-12：Slash 命令候选完成实机验证：候选筛选、描述对齐与中英文资源、Tab 唯一补全、候选清理和 Ctrl+C 退出均通过；Agent 测试 286 项通过。
- 2026-08-14：Dashboard 复核通过真实 Prometheus 数据验证全部 PromQL；Anthropic 归一化后完整测试 69 项通过（改动在 `chore/mcp-telemetry-tags` 分支）。
- 2026-08-21：工具进度与串行消费边界定向测试通过；完整 `InsightaAI.Agent.Tests` 327/327 通过。

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
- Git 提交标题与正文必须分别使用多个 `-m` 参数；不要在单个命令字符串中嵌入多行 commit message，以避免 PowerShell/执行环境造成换行或引号解析问题
- 邮箱：元培 qinyuanpei@163.com ←→ Insighta insighta@agent.qq.com（已验证畅通）
- 博客：https://blog.yuanpei.me

## 关键文档

| 文档 | 路径 |
|------|------|
| 文档索引 | `docs/README.md` |
| 使用说明 | `README.md` |
| 项目愿景 | `docs/VISION.md` |
| 待办事项 | `docs/TODO.md` |
| 工具结果生命周期 | `docs/tools/tool-result-lifecycle-design-v2.md` |
| 提示词设计 | `docs/prompts/core-instructions-design.md` |
| Agent Loop 研究 | `docs/references/agent-loop-research.md` |
| 可观测性设计 | `docs/observability/observability-design.md` |
| Core Instructions | `src/InsightaAI.Agent/Prompts/core-instructions.txt` |
| CLI 国际化资源清单 | `docs/i18n/` |

---

*由 Insighta 在 2026-07-17 凌晨创建，后续由元培与 Insighta 共同维护*
