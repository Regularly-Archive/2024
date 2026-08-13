# InsightaAI Agent - Observability Design Document

## 1. Background & Problem

Agent 运行涉及多个异步层次（turn → round → LLM request → tool call），排查问题困难：

- **缺少 trace 链路**：无法追踪一次用户请求经过哪些 LLM 调用和工具执行
- **缺少 metrics**：看不到 token 消耗、延迟分布、缓存命中率
- **AsyncLocal 丢失**：`IAsyncEnumerable` yield 边界导致 `Activity.Current` 丢失，子 Activity 无法正确挂载

## 2. Design Goals

1. **OpenTelemetry 原生**：使用 System.Diagnostics 内置 API，零额外依赖（仅 OTel API）
2. **装饰器模式**：不侵入 Agent 核心逻辑，通过 proxy/wrapper 注入
3. **跨 AsyncLocal 边界**：通过静态字典解决 yield 边界 Activity.Current 丢失问题
4. **分层 Span 树**：turn → round → llm_request / tool_call

## 3. Architecture Overview

```
TelemetryConstants                    # 集中管理 ActivitySource、Meter、Counter、Histogram
├── ActivitySource "InsightaAI.Agent"
├── Meter "InsightaAI.Agent"
├── Counters: input/output/cache_hit tokens, agent runs
├── Histograms: llm duration, tool duration, round duration
└── CurrentRoundContext               # ConcurrentDictionary<string, ActivityContext>

AgentEventTelemetryHook               # IAgentEventHook 实现
├── OnAgentTurnStartedAsync           → 创建 turn span，记录 agent.id/model
├── OnAgentRoundStartedAsync          → 创建 round span，写入字典供 proxy 查找
├── OnAgentRoundEndedAsync            → 关闭 round span，记录 round.duration_ms
└── OnAgentTurnEndedAsync             → 关闭 turn span，清理字典

LlmClientTelemetryProxy               # ILlmClient 装饰器
├── Streaming()                       → 创建 llm_request span，包装 LlmStream
└── CompleteAsync()                   → 创建 llm_request span，记录 token/duration

LlmStreamTelemetryProxy               # LlmStream 装饰器
├── GetAsyncEnumerator()              → 流遍历，finally 停止计时
├── GetResponseAsync()                → 记录 token usage + 关闭 span
└── Dispose()                         → 兜底关闭 span

ToolCallHandlerTelemetryWrapper       # ToolCallHandler 委托装饰器
└── Wrap()                            → 创建 tool_call span，记录 duration/error

AgentTelemetryExtensions              # 便捷扩展方法
├── AddTelemetry()                    → 一步启用所有 telemetry
├── WithTelemetry(ILlmClient)         → 单独装饰 LLM client
└── WithTelemetry(ToolCallHandler)    → 单独装饰 tool handler
```

## 4. Span Hierarchy

```
insighta.agent.turn_start
├── insighta.agent.round (round 1)
│   ├── insighta.llm.request
│   │   └── gen_ai.usage.* (tags + metrics)
│   ├── insighta.agent.tool_call (bash)
│   └── insighta.agent.tool_call (read_file)
├── insighta.agent.round (round 2)
│   ├── insighta.llm.request
│   └── insighta.agent.tool_call (glob)
└── insighta.agent.turn_end
```

## 5. Key Design Decisions

### 5.1 AsyncLocal 丢失问题

`IAsyncEnumerable` yield 边界会导致 `Activity.Current` 丢失或指向错误的 Activity：

```csharp
// AgentLoop.RunAsync() 中创建 round span
_roundActivity = ActivitySource.StartActivity("insighta.agent.round");

// yield return 后到达 Agent.RunStreamAsync()
await foreach (var evt in agentLoop.RunAsync(context, token)) { ... }

// 随后 tool call handler 在同一 round 内执行，
// 但 Activity.Current 已丢失，无法找到 round span 作为 parent
```

**解决方案**：`AgentEventTelemetryHook` 在 `OnAgentRoundStartedAsync` 时将 round ActivityContext 写入静态 `ConcurrentDictionary<string, ActivityContext>`（key 为 agentId）。Proxy/Wrapper 构造时持有 agentId，执行时从字典恢复 parent context：

```csharp
// LlmClientTelemetryProxy.StartChildActivity()
ActivityContext roundActivityContext = default;
if (_agentId != null)
    roundActivityContext = TelemetryConstants.CurrentRoundContext[_agentId];

return TelemetryConstants.ActivitySource.StartActivity(
    name, ActivityKind.Client, parentContext: roundActivityContext);
```

### 5.2 Turn Span 生命周期

Turn span 在 `OnAgentTurnStartedAsync` 中创建并立即 Dispose。设计意图是让 turn span 仅作为 trace 链路的锚点，实际时长通过 `turn_end` span 记录。持久会话仍通过 `session.id` 关联多个 turn。

### 5.3 Metrics 命名

遵循 OpenTelemetry GenAI 语义约定：

| Instrument | Name | Type |
|---|---|---|
| InputTokenCounter | `gen_ai.client.tokens.input` | Counter\<long\> |
| OutputTokenCounter | `gen_ai.client.tokens.output` | Counter\<long\> |
| CacheHitTokenCounter | `gen_ai.client.tokens.cache_hit` | Counter\<long\> |
| AgentRunCounter | `insighta.agent.run.total` | Counter\<long\> |
| AgentRoundCounter | `insighta.agent.round.total` | Counter\<long\> |
| SkillActivationCounter | `insighta.skill.activation` | Counter\<long\> |
| LlmRequestDuration | `gen_ai.client.operation.duration` | Histogram\<double\> |
| ToolExecutionDuration | `insighta.tool.execution.duration` | Histogram\<double\> |
| AgentRoundDuration | `insighta.agent.round.duration` | Histogram\<double\> |

## 6. Usage

```csharp
// 一步启用
var agent = new Agent(config, llmClient, toolRegistry);
agent.AddTelemetry(sessionId);

// 或单独装饰
var instrumentedClient = llmClient.WithTelemetry();
```

## 7. Prometheus 与 Grafana 本地验证（2026-08-13）

本地观测栈通过 OpenTelemetry Collector 接收 OTLP；trace 转发到 Jaeger，metric 由 Prometheus 抓取，再由 Grafana 查询。部署与启动方式见 [`../tools/observability/README.md`](../tools/observability/README.md)。

已由真实 CLI 会话验证的 Prometheus 指标及其低基数维度：

| 指标 | 已验证标签 | 用途 |
|---|---|---|
| `gen_ai_client_operation_duration_milliseconds` | `gen_ai_adapter`、`gen_ai_system`、`gen_ai_request_model` | LLM 请求延迟 |
| `gen_ai_client_tokens_{input,output,cache_hit}_total` | 同上 | 模型维度 token 消耗趋势 |
| `insighta_tool_execution_duration_milliseconds` | `gen_ai_tool_name`、`gen_ai_tool_is_error`、`gen_ai_tool_is_allowed` | 工具延迟、失败率与权限拒绝 |
| `insighta_agent_run_runs_total` | `agent_id`、`gen_ai_request_model` | 完成的 Agent Turn 数 |
| `insighta_agent_round_rounds_total` | `agent_id`、`gen_ai_request_model` | Agent Round 数与每 Turn 平均 Round 数 |
| `insighta_agent_round_duration_milliseconds` | `agent_id`、`gen_ai_request_model` | Round 延迟 |
| `insighta_skill_activation_activations_total` | `insighta_skill_name` | 成功激活 Skill 的次数 |

Prometheus exporter 将 OTel attribute 中的点号转换为下划线，并为 Counter / unit 补充 `_total` / `_milliseconds` 等后缀。因此 Grafana 与 PromQL 必须以 Prometheus 实际暴露的名称和 label 为准，而不是只按 .NET instrument 名推测。

### 已发现问题：`round_number` 是高基数标签

`insighta_agent_round_duration_milliseconds` 曾包含 `round_number`。轮次在每个 Turn 中递增，长期运行会持续创建新的 time series，不适合作为 Prometheus label。

修复：Round Histogram 的 metric tag 只保留 `agent_id` 与 `gen_ai_request_model`；新增低基数 `insighta_agent_round_rounds_total` Counter。`round.number` 继续保留在 Jaeger round span attribute，供单条 trace 排障。Dashboard 不依赖轮次标签。

## 8. Dashboard v2 待办

当前 `InsightaAI Overview` 只有 Tool p95、Round p95、Token rate 和 Tool error rate 四个验证面板。后续 Dashboard v2 按以下优先级演进：

1. **指标基数（已完成）**：Round metric 已移除 `round_number`，并新增低基数 Round counter；持续禁止 `sessionId`、`userId`、请求 ID、工具参数与 endpoint 进入 Prometheus label。
2. **总览 Stat**：Turn 数、LLM 请求数、总 input/output token、工具错误率；错误率以百分比显示。
3. **LLM**：按 `gen_ai_request_model` 分组展示请求速率、p50/p95 延迟及 input/output token 速率。
4. **Agent**：Round p50/p95 与每 Turn 平均 Round 数；后者需要新增低基数计数器或从 trace 派生，不能使用 `round_number` 标签。
5. **Tool**：独立的 `InsightaAI Tools` Dashboard 按 `gen_ai_tool_name` 展示 p50/p95（使用 Grafana 自适应的 `$__rate_interval`）、成功率最高的工具与失败率最高的工具；成功/失败率的分子和分母都限定 `gen_ai_tool_is_allowed=true`，权限拒绝不视为工具故障。并按 `insighta_skill_name` 展示成功激活次数最多的 Skill。Skill 只在 `activate_skill` 被允许且成功时计数，不把失败、拒绝或未找到的 Skill 计入。

Skill 面板使用 `max_over_time(counter[$__range])` 而不是 `increase()`：OTel Counter 的首次 `Add(1)` 可能早于 Prometheus 首次 scrape，导致窗口内没有可供 `increase()` 计算的跳变；取各进程时序在范围内的累计最大值可以展示此类首次激活。面板因此表示所选时间范围内可见的进程累计激活量，而非严格的时间窗口增量。
6. **MCP**：当前只在 Jaeger trace 中使用 `mcp.server.*`、`mcp.config.*`、`mcp.method.name` 排障。若要进入 metric，只新增稳定低基数的 server/config/method 名，不带 description、endpoint、arguments、session 或 user。
7. **Trace 关联**：将慢/失败面板与 Jaeger 查询关联，定位某一 Turn、Round 或 MCP 调用。

### 成本与行为面板（已加入 Dashboard v5）

- **Input cache hit ratio**：当前时间范围内 `cache_hit_tokens / input_tokens`。它反映缓存复用效率；缓存 token 通常价格更低，但最终账单仍取决于各模型供应商的计费规则。
- **Uncached input tokens**：`input_tokens - cache_hit_tokens`，用于估算未享受缓存折扣的输入规模，不等同于货币金额。
- **Input : output token ratio**：输入与输出总量的比值，用于观察 Agent 的读写行为是否发生明显漂移。
- **Tool call distribution**：按工具名计算调用数占比，用于区分 bash、文件、搜索和其他工具主导的工作模式。

`service_instance_id` 代表一次 CLI/进程实例，不是持久会话 ID；`sessionId`、`userId` 继续禁止作为 Prometheus label。每 Turn 的 Round 数分位数也不能由当前聚合 Counter 反推，若有需求需引入独立低基数 Histogram。

## 9. Dependencies

- `OpenTelemetry.Api` (v1.16.0) — 仅 API，不引入 SDK/Exporter
- 项目引用：`InsightaAI.Agent`
