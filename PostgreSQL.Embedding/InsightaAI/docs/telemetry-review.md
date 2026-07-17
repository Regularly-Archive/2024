# Insighta 遥测实现 Review

## 当前架构概览

Insighta 的遥测实现在 `InsightaAI.Agent.Diagnostics` 项目中，包含以下核心文件：

- `TelemetryConstants.cs` - ActivitySource、Meter 和指标定义
- `AgentTelemetryHook.cs` - Session/Round 级别的 span 管理
- `TelemetryLlmClient.cs` - LLM 调用的装饰器
- `TelemetryLlmStream.cs` - 流式响应的装饰器
- `TelemetryToolCallHandler.cs` - 工具调用的装饰器
- `AgentTelemetryExtensions.cs` - 便捷扩展方法

---

## 做得好的地方

### 1. 分层架构清晰
Session → Round → LLM/Tool 三级 span 层级，符合 Agent 执行流程。

### 2. 装饰器模式
- `TelemetryLlmClient` 装饰 `ILlmClient`
- `TelemetryToolCallHandler` 包装 `ToolCallHandler` 委托

不侵入业务代码，可插拔。

### 3. 解决 AsyncLocal 丢失问题
用 `ConcurrentDictionary<string, Activity>` 绕过 `IAsyncEnumerable` yield 边界丢失 `Activity.Current` 的问题。这是 C# 异步流的已知坑，处理得很好。

### 4. 指标覆盖全面
- Token usage：input、output、cache_hit 三个 Counter
- Duration：LLM request、tool execution、agent round 三个 Histogram
- Agent run 计数器

---

## 需要改进的地方

### 1. 属性命名不符合 OTel GenAI Conventions

**当前用的是自定义命名：**

| 当前属性 | OTel GenAI Convention | 说明 |
|---------|----------------------|------|
| `gen_ai.adapter` | `gen_ai.system` | 应使用 OTel 标准的 system 属性 |
| `gen_ai.request.model` | `gen_ai.request.model` | ✅ 符合 |
| `llm.stream` | `gen_ai.request.is_stream` | 应使用标准命名 |
| `llm.duration_ms` | `gen_ai.client.operation.duration` | 应使用标准 metric 名 |
| `tool.name` | `gen_ai.tool.name` 或 `mcp.method.name` | 应使用标准命名 |
| `tool.args` | `gen_ai.tool.arguments` | 应使用标准命名 |
| `tool.is_error` | 无标准，可保留 | 自定义属性 |

**建议：** 尽早统一为 OTel GenAI conventions，避免后续迁移成本。

**现有 Metric 命名映射：**

| 当前 metric 名 | OTel GenAI Convention | 代码位置 |
|---------------|----------------------|---------|
| `insighta.llm.tokens.input` | `gen_ai.client.token.usage` (token.type=input) | `TelemetryConstants.cs` L30-31 |
| `insighta.llm.tokens.output` | `gen_ai.client.token.usage` (token.type=output) | `TelemetryConstants.cs` L32-33 |
| `insighta.llm.tokens.cache_hit` | 无标准，可保留 | `TelemetryConstants.cs` L34-35 |
| `insighta.llm.request.duration` | `gen_ai.client.operation.duration` | `TelemetryConstants.cs` L40-41 |
| `insighta.tool.execution.duration` | 无标准，可保留 | `TelemetryConstants.cs` L42-43 |
| `insighta.agent.round.duration` | 无标准，可保留 | `TelemetryConstants.cs` L44-45 |
| `insighta.agent.run.total` | 无标准，可保留 | `TelemetryConstants.cs` L36-37 |

### 2. 缺少 MCP 相关 span

OTel v1.39+ 新增了 MCP tracing 属性：
- `mcp.method.name` - MCP 方法名
- `mcp.session.id` - MCP 会话 ID
- `mcp.protocol.version` - MCP 协议版本

MCP 是 Insighta 的核心能力之一（支持 MCP Server 和 MCP Tool），应该为 MCP 调用添加专门的 span。

**MCP 相关代码位置：**
- `src/InsightaAI.Agent/Mcp/` - MCP 核心实现
  - `McpRegistry.cs` - MCP Server 注册表
  - `IMcpConnectionPool.cs` - 连接池接口
  - `IMcpServerProvider.cs` - Server Provider 接口
- `src/InsightaAI.Agent/Tools/BuiltIn/McpTools.cs` - MCP 工具实现
- `src/InsightaAI.Agent.Cli/Commands/McpCommand.cs` - MCP 命令行命令

**建议：** 在 `TelemetryToolCallHandler` 中检测工具类型，如果是 MCP 工具，添加 `mcp.*` 属性。

### 3. 缺少关键告警指标

建议添加以下指标用于告警：

| 指标 | 告警条件 | 用途 |
|------|---------|------|
| 输入/输出 token 比 | > 10:1 持续 | 检测 system prompt 是否过大 |
| 单次 run token 总量 | > 2x 基线持续 10min | 检测死循环或 prompt 注入 |
| Error rate | > 2% 持续 5min | 检测限流或配额耗尽 |
| Agent round 次数 | > 阈值 | 检测无限循环 |

### 4. 缺少 prompt/completion 内容捕获

当前只记录了 token 数量和 duration，没有记录实际的 prompt 和 completion 内容。

**用途：**
- 调试：重现问题需要完整的上下文
- 审计：合规要求记录交互内容
- 评估：分析 Agent 输出质量

**注意：** 需要考虑隐私，建议添加配置开关（默认关闭）。

### 5. 缺少 cost 估算

可以根据 token 数量和模型单价估算成本，这对运营很有价值。

**示例：**
```csharp
// 根据模型和 token 数量估算成本
var cost = EstimateCost(model, inputTokens, outputTokens);
activity?.SetTag("gen_ai.usage.cost_usd", cost);
```

### 6. 缺少 user/session 维度的聚合指标

当前指标只按 `agent.id` 和 `model` 分组，建议添加：
- 按 user 分组的 token 消耗
- 按 session 分组的交互次数
- 按 user 分组的错误率

### 7. 错误处理不完整

`AgentTelemetryHook.cs` 的 `EndRoundActivity()` 方法中，round span 总是设置为 `ActivityStatusCode.Ok`，没有检查 `_lastError`。

**当前代码（L201）：**
```csharp
_roundActivity.SetStatus(ActivityStatusCode.Ok);
```

**建议修改：**
```csharp
if (_lastError != null)
{
    _roundActivity.SetStatus(ActivityStatusCode.Error, _lastError.Message);
    _roundActivity.SetTag("error.type", _lastError.GetType().Name);
    _roundActivity.SetTag("error.message", _lastError.Message);
}
else
{
    _roundActivity.SetStatus(ActivityStatusCode.Ok);
}
```

**影响：** 当前实现会导致有错误的 round 在 trace 中显示为成功，影响问题排查。

---

## 建议优先级

### 高优先级（尽快处理）
1. **统一属性命名为 OTel GenAI conventions** - 避免后续迁移成本
2. **添加 MCP span** - MCP 是核心能力，应该有完整的 tracing
3. **修复错误处理** - `EndRoundActivity()` 中 round span 未检查 `_lastError`，导致错误 round 显示为成功

### 中优先级（下一迭代）
4. **添加关键告警指标** - token 比率、error rate、round 次数
5. **添加 cost 估算** - 运营价值高

### 低优先级（后续规划）
6. **添加 prompt 内容捕获** - 需要配置开关，默认关闭
7. **添加 user/session 维度指标** - 需要设计 user 标识方案

---

## 参考资源

- [OpenTelemetry GenAI Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/)
- [OpenTelemetry for AI Agents: Implementing Observability in MCP Workflows](https://www.mintmcp.com/blog/opentelemetry-ai-agents)
- [AI Agent Observability: Tracing & Monitoring in 2026](https://www.digitalapplied.com/blog/ai-agent-observability-2026-tracing-monitoring-stack-guide)
- [OpenTelemetry for AI Systems: LLM and Agent Observability (2026)](https://uptrace.dev/blog/opentelemetry-ai-systems)

---

*Review 日期：2026-07-15*
*Reviewer：Insighta*
