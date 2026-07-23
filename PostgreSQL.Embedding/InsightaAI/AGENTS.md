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
Agent.cs (606行)        → 初始化、依赖管理、系统提示词、事件消费、Hook 触发
AgentLoop.cs (319行)    → 核心循环（LLM 调用 → 工具执行 → 消息累积）
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
- `IAgentHook` — Agent 生命周期拦截（会话启动、记忆抽取）

## 最近提交（2026-07-23）

```
f1f4bff feat(cli): introduce localization for config command and split config into subcommands
5bb3c69 refactor(agent): implement 4-layer system prompt architecture with AGENTS.md support
c321d78 fix: resolve naming inconsistencies in telemetry constants
ce53a2b feat: add OpenTelemetry diagnostics; refactor orchestrator namespace
0822f42 feat: show inline diff preview for edit_file in permission hook
f806a75 refactor: extract AgentLoop and add auto message persistence
```

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
- `_activatedSkills` List<ISkill> 替代 `_skillInstructions` 字符串累加
- `LoadAgentsMd()` 懒加载，仅读一次

### MCP 工具调用元数据管道（2026-07-21）

- `ToolResult` 新增 `Metadata` 属性（`IReadOnlyDictionary<string,object?>?`）
- `McpToolCallResult` 封装 `Text` + `IsError` + `Metadata`，替代 `IMcpConnectionPool.CallToolAsync` 的 `string` 返回值
- 两层填充：`SimpleMcpConnectionPool` 填 server 身份（`mcp.server.name`/`version`），`McpRegistry` 填本地配置（`mcp.server.description`/`transport`）
- `ToolCallHandlerTelemetryWrapper` 统一消费 Metadata → `activity.SetTag`
- `AgentLoop.HandleMaxRoundsExceededAsync` 修复：传入 `snapshot` 而非 `context.Messages`

**遗留：** MCP Telemetry Tag 命名优化（`mcp.server.description` → `mcp.config.description`，去重 `mcp.server.transport`），见 TODO.md #12

### 统一摘要服务与会话标题（2026-07-21）

- 新增 `Context/Summary/ISummaryService` 与 `SummaryService`，统一承载三种轻量 LLM 场景：
  - `SummarizeAsync` — 全量摘要，供 `TraditionalCompactStrategy` 使用
  - `UpdateAsync` — 增量摘要，供 `SessionMemoryHook` 使用
  - `GenerateTitleAsync` — 根据新会话首条用户消息生成短标题
- `SummaryResult` 显式返回成功状态、`FinishReason`、尝试次数和错误，摘要失败时不再保存残缺内容
- 全量/增量摘要统一使用 `summary-output-template.txt`，固定 Goal、Constraints、Progress、Key Decisions、Next Steps、Critical Context、Relevant Files 等章节
- 摘要遇到 `DoneReason.MaxTokens` 时执行一次更激进的完整重生成；连续失败则保留旧 Session Memory 或放弃本次 TraditionalCompact
- 标题生成默认 256 tokens；无正文且命中 `MaxTokens` 时以 512 tokens 重试，并接受已生成的可用短标题
- 标题 LLM 连续失败时，从首条用户输入确定性降级：取第一行、清理 Markdown/空白、按 Unicode 字符安全截断到 30 字符并添加省略号
- `SessionMemoryHook` 移除模型/客户端工厂等重复配置，改为依赖 `ISummaryService`；`MinRoundsBeforeLlm` 现在实际生效
- `IMessageStorage.UpdateSessionTitleAsync` 只原子更新标题和时间，避免与消息计数并发更新相互覆盖；JSONL 与 PostgreSQL 均已实现
- CLI 在首条普通用户消息时并行生成标题，`insighta sessions` 增加 Title 列

**关键文件：**
- `src/InsightaAI.Agent/Context/Summary/` — 摘要服务接口、实现、配置和结果模型
- `src/InsightaAI.Agent/Prompts/full-summary.txt` — 全量摘要 Prompt
- `src/InsightaAI.Agent/Prompts/incremental-summary.txt` — 增量摘要 Prompt
- `src/InsightaAI.Agent/Prompts/summary-output-template.txt` — 共享输出结构
- `src/InsightaAI.Agent/Prompts/session-title.txt` — 会话标题 Prompt
- `tests/InsightaAI.Agent.Tests/Context/SummaryServiceTests.cs` — 摘要、MaxTokens、标题和 fallback 测试

### CLI 全命令国际化（2026-07-23）

- `ChatCommand`、`ChatRenderer`、`EventRenderer` 中 41 处硬编码字符串提取到 resx
- `CliStrings.resx` / `CliStrings.zh-CN.resx` 新增 41 个 `Chat*` 资源条目（21 个静态属性 + 20 个 Format 格式化）
- Spectre.Console markup 标签整体存入 resx 值中，翻译时保留 `[yellow]`、`[dim]` 等标签
- `ask_user` 工具的 `question` 参数增加 `Markup.Escape` 防护
- 5 个命令的国际化资源清单文档存放在 `docs/i18n/`
- 全部 5 个 CLI 命令（config、sessions、mcp、skills、chat）已完成国际化

**关键文件：**
- `src/InsightaAI.Agent.Cli/Resources/CliStrings.resx` — 默认英文资源
- `src/InsightaAI.Agent.Cli/Resources/CliStrings.zh-CN.resx` — 中文资源
- `src/InsightaAI.Agent.Cli/Localization/CliStrings.cs` — 资源访问入口
- `docs/i18n/` — 5 个命令的国际化资源清单文档

## 当前问题与改进方向

### 已知问题

1. **MicroCompact 生命周期重构（已完成核心链路）** — 阈值调整为 45-65-80，工具结果按 Full → Preview → Placeholder → Removed 渐进降级；Artifact 与上下文表示分离。策略先在消息副本上试算，有实际收益才提交；自动压缩可按阈值级联，手动 `/compact auto` 按优先级提交第一个有效策略。

2. **Memory 全量注入** — `GetMemoryIndexAsync` 返回全量 MEMORY.md 文本（80+ 条），改轻量为统计信息 + `search_memory` 工具按需检索。

3. **摘要服务统一（已完成）** — 全量摘要、增量摘要和会话标题已统一到 `SummaryService`；共享结构模板，并具备 MaxTokens 重试、完整性校验与标题 fallback。

4. **AgentBuilder 生命周期** — 构造函数未默认注册 `ToolRegistry`，用户不调用 `WithToolRegistry()` 会抛异常。

### TODO.md 重点项

- [x] 摘要服务统一（全量/增量摘要、会话标题、MaxTokens 恢复与 fallback）
- [x] MicroCompact 阈值优化与工具结果生命周期重构（45-65-80）
- [x] CLI 全命令国际化（ChatCommand + ChatRenderer + EventRenderer，41 处字符串提取到 resx）
- [ ] Memory 轻量化索引
- [ ] AgentBuilder 默认注册 `ToolRegistry`
- [ ] L3 Orchestrator 继续开发
- [ ] MCP Telemetry Tag 命名清理（`mcp.server.description`→`mcp.config.description`，去重 transport）

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
