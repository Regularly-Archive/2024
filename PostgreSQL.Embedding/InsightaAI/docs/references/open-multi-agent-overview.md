# Open Multi-Agent 框架概览

## 项目简介
Open Multi-Agent 是面向 TypeScript 后端的**目标优先（goal-first）多智能体编排框架**。给定一个目标，协调者 agent 会将其拆解为任务 DAG（有向无环图），并行执行独立任务，合成最终结果。

### 核心理念
- **目标优先**：工程师只描述目标，不画任务图。框架在运行时构建任务 DAG，编排随目标自适应。
- **轻量级**：仅 3 个运行时依赖，可直接嵌入任意现有 Node.js 后端。
- **类型安全**：原生 TypeScript 实现。

## 三种运行模式

| 模式 | 方法 | 适用场景 |
|------|------|----------|
| **单智能体** | `runAgent()` | 一个智能体，一个提示词，最简入口 |
| **自动编排团队** | `runTeam()` | 给一个目标，框架自动规划和执行 |
| **显式任务管线** | `runTasks()` | 你自己定义任务图和分配 |

### 示例用法
```typescript
import { OpenMultiAgent, type AgentConfig } from '@open-multi-agent/core'

const agents: AgentConfig[] = [
  { name: 'architect', model: 'claude-sonnet-4-6', systemPrompt: 'Design clean API contracts.' },
  { name: 'developer', model: 'claude-sonnet-4-6', systemPrompt: 'Implement runnable TypeScript.' },
  { name: 'reviewer', model: 'claude-sonnet-4-6', systemPrompt: 'Review correctness and security.' },
]

const orchestrator = new OpenMultiAgent()
const team = orchestrator.createTeam('api-team', { agents, sharedMemory: true })

// 给定目标，自动生成任务 DAG 并执行
const result = await orchestrator.runTeam(team, 'Create a REST API for a todo list')
```

## 核心功能

### 1. 目标驱动协调
- 一个 `runTeam(team, goal)` 调用，把目标拆成任务 DAG
- 并行执行独立任务，合成最终结果
- 支持多种任务调度策略：dependency-first、round-robin、least-busy、capability-match

### 2. 多 Provider 支持
- 12 家内置 provider（Anthropic、Gemini、OpenAI、Azure、DeepSeek 等）
- 支持任意 OpenAI 兼容端点（Ollama、vLLM、LM Studio 等）
- 同队可混用不同 provider

### 3. 工具与 MCP 集成
- 6 个内置工具：bash、file_*、grep、glob
- **默认拒绝（default-deny）**：每个 agent 只能使用明确授予的工具
- 支持通过 `defineTool()` + Zod 自定义工具
- 可接入任意 MCP server

### 4. 编排控制
- **人工介入**：`onPlanReady`（执行前审批整个计划）、`onApproval`（每轮任务之间审批）
- **固定并重放计划**：`createPlanArtifact` 序列化任务图，`runFromPlan` 重放
- **取消运行**：通过 `AbortSignal` 中途取消
- **可配置协调者**：单独指定 model、adapter 或额外指令

### 5. 可观测性
- `onProgress` 事件、`onTrace` span
- 运行结束后渲染任务 DAG 的 HTML dashboard
- API key 和 token 自动脱敏

### 6. 生产级控制
- 上下文策略（sliding-window、summarize、compact）
- 任务重试退避
- 循环检测
- 工具输出截断/压缩
- 总额封顶（maxTokenBudget）

## 架构概览
```
┌─────────────────────────────────────────────────────────────────┐
│ OpenMultiAgent (Orchestrator)                                   │
│                                                                 │
│  createTeam()  runTeam()  runTasks()  runAgent()  getStatus()   │
└──────────────────────┬──────────────────────────────────────────┘
                       │
┌──────────▼──────────┐
│ Team                │
│ - AgentConfig[]     │
│ - MessageBus        │
│ - TaskQueue         │
│ - SharedMemory      │
└──────────┬──────────┘
           │
┌──────────▼──────────┐  ┌──────────────────────┐
│ Agent               │──►│ LLMAdapter           │
│ - run()             │  │ - 12 built-in        │
│ - prompt()          │  │   providers          │
│ - stream()          │  │ - OpenAI-compatible  │
└──────────┬──────────┘  └──────────────────────┘
           │
┌──────────▼──────────┐  ┌──────────────────────┐
│ AgentRunner         │──►│ ToolRegistry         │
│ - conversation loop │  │ - defineTool()       │
│ - tool dispatch     │  │ - 6 built-in tools   │
└──────────────────────┘  │ + delegate (opt-in)  │
                          └──────────────────────┘
```

## 生态与集成

### 生产环境使用
- **temodar-agent**（约 60 stars）：WordPress 安全分析平台，使用内置工具（bash、file_*、grep）

### 集成项目
- **Engram**：AI 记忆的 Git，跨 agent 同步知识
- **@agentsonar/oma**：Sidecar，检测委派环、重复和速率突增

### 示例分类
- **真实业务流程**（cookbook/）：合同审阅、会议总结、竞品监测、翻译回译等
- **模式与集成**：结构化输出、多视角代码评审、跨 provider 推理、成本分级流水线等

## 快速开始
```bash
npm install @open-multi-agent/core
export ANTHROPIC_API_KEY=sk-...
npx tsx examples/basics/team-collaboration.ts
```

## 文档资源
- Provider 配置：docs/providers.md
- 工具配置：docs/tools.md
- 可观测性：docs/observability.md
- 共享记忆：docs/memory.md
- 上下文管理：docs/context.md
- CLI：docs/cli.md
- 模型路由：docs/model-routing.md

## 许可证
MIT 协议（2026年4月1日发布）