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
| 1 | MicroCompact | 55% | 替换旧 tool_result（保留参数，摘要内容） |
| 2 | SessionMemoryCompact | 65% | Anchored Summary + 生成会话记忆 |
| 3 | TraditionalCompact | 75% | 全文摘要替代历史消息 |

### 上下文配置

```yaml
MaxContextTokens: 64,000
ReservedForOutput: 16,384
MicroCompactThreshold: 55%
SessionCompactThreshold: 65%
TraditionalCompactThreshold: 75%
```

### Hook 体系

- `IToolHook` — 工具执行前后拦截（权限控制、日志）
- `IAgentHook` — Agent 生命周期拦截（会话启动、记忆抽取）

## 最近提交（2026-07-17）

```
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
Layer 3: AgentConfig.SystemPrompt 用户定制指令
Layer 4: Dynamic Context          Skills / MCP / Memory（每轮重建）
```

**关键文件**：
- `Prompts/core-instructions.txt` — Layer 1 静态规则
- `Context/SystemPrompt/SystemPromptBuilder.cs` — 纯函数组装器
- `Context/SystemPrompt/SystemPromptParams.cs` — 输入参数
- `Agent.cs:BuildSystemPromptAsync()` — 每轮调用 Builder

### Skills & MCP 动态管理

- 已激活 Skill 从 available 列表自动排除（去重）
- `_activatedSkills` List<ISkill> 替代 `_skillInstructions` 字符串累加
- `LoadAgentsMd()` 懒加载，仅读一次

### MCP 工具调用元数据管道（2026-07-21）

- `ToolResult` 新增 `Metadata` 属性（`IReadOnlyDictionary<string,object?>?`）
- `McpToolCallResult` 封装 `Text` + `IsError` + `Metadata`，替代 `IMcpConnectionPool.CallToolAsync` 的 `string` 返回值
- 两层填充：`SimpleMcpConnectionPool` 填 server 身份（`mcp.server.name`/`version`），`McpRegistry` 填本地配置（`mcp.server.description`/`transport`）
- `ToolCallHandlerTelemetryWrapper` 统一消费 Metadata → `activity.SetTag`
- `AgentLoop.HandleMaxRoundsExceededAsync` 修复：传入 `snapshot` 而非 `context.Messages`

**遗留：** MCP Telemetry Tag 命名优化（`mcp.server.description` → `mcp.config.description`，去重 `mcp.server.transport`），见 TODO.md #12

## 当前问题与改进方向

### 已知问题

1. **MicroCompact 收益递减** — 只压缩内容不减少消息数，多次压缩后失效，阈值间距过密（55%→65%→75%）。讨论中的改进方向：降至 45-65-80，增加最小收益检查避免无用压缩。

2. **Memory 全量注入** — `GetMemoryIndexAsync` 返回全量 MEMORY.md 文本（80+ 条），改轻量为统计信息 + `search_memory` 工具按需检索。

3. **摘要服务重复** — `TraditionalCompactStrategy.GenerateSummaryAsync` 与 `SessionMemoryHook.GenerateAnchoredSummaryAsync` 存在 LLM 调用、摘要提取等重复代码，待提取到公共 `SummaryService`。

4. **AgentBuilder 生命周期** — 构造函数未默认注册 `ToolRegistry`，用户不调用 `WithToolRegistry()` 会抛异常。

### TODO.md 重点项

- [ ] 摘要服务统一（`CompactionHelper` 或 `SummaryService`）
- [ ] MicroCompact 阈值优化（45-65-80）
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
- 博客：https://blog.yuanpei.me

## 关键文档

| 文档 | 路径 |
|------|------|
| 项目愿景 | `docs/VISION.md` |
| 待办事项 | `docs/TODO.md` |
| 提示词设计 | `docs/core-instructions-design.md` |
| Agent Loop 研究 | `docs/agent-loop-research.md` |
| 可观测性设计 | `docs/observability-design.md` |
| Core Instructions | `src/InsightaAI.Agent/Prompts/core-instructions.txt` |

---

*由 Insighta 在 2026-07-17 凌晨创建，后续由元培与 Insighta 共同维护*
