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

## 7. Dependencies

- `OpenTelemetry.Api` (v1.16.0) — 仅 API，不引入 SDK/Exporter
- 项目引用：`InsightaAI.Agent`
