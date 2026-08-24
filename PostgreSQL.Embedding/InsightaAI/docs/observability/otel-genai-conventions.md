# OpenTelemetry GenAI Semantic Conventions 规范整理

> 整理日期：2026-07-15
> 状态：Experimental（实验性，尚未稳定）
> 版本：v1.41+

---

## 概述

OpenTelemetry GenAI Semantic Conventions 是由 OpenTelemetry GenAI SIG（Special Interest Group）制定的标准，用于统一 LLM 和 AI Agent 的可观测性数据格式。该规范定义了 Span 属性、Metric 和 Event 的标准命名，确保不同厂商的遥测数据可以互通。

**当前状态：**
- 2024年4月开始制定
- 截至2026年7月，仍处于 `experimental` 状态
- 主要厂商（Datadog、Grafana、Elastic）已开始支持
- 约15%的 GenAI 部署使用了 observability

**版本管理：**
- 使用 `OTEL_SEMCONV_STABILITY_OPT_IN` 环境变量管理版本过渡
- 设置 `gen_ai_latest_experimental` 启用最新实验性特性

---

## 核心 Span 属性

### 通用属性

| 属性 | 类型 | 必填 | 说明 | 示例 |
|------|------|------|------|------|
| `gen_ai.system` | string | 是 | LLM 提供商 | `openai`, `anthropic`, `google_vertex_ai` |
| `gen_ai.operation.name` | string | 是 | 操作类型 | `chat`, `text_completion`, `embeddings` |
| `gen_ai.request.model` | string | 否 | 请求的模型名 | `gpt-4o`, `claude-3-7-sonnet` |
| `gen_ai.response.model` | string | 否 | 实际使用的模型（可能不同） | `gpt-4o-2024-11-20` |
| `gen_ai.request.temperature` | float | 否 | 采样温度 | `0.7` |
| `gen_ai.request.max_tokens` | int | 否 | 最大 token 数 | `1024` |
| `gen_ai.request.is_stream` | boolean | 否 | 是否流式请求 | `true` |

### Token 用量属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `gen_ai.usage.input_tokens` | int | 输入/prompt token 数 |
| `gen_ai.usage.output_tokens` | int | 输出/completion token 数 |
| `gen_ai.usage.cache_hit_tokens` | int | 缓存命中 token 数（非标准，可扩展） |

### 响应属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `gen_ai.response.finish_reason` | string | 完成原因 | `stop`, `length`, `content_filter` |

---

## gen_ai.system 枚举值

| 值 | 说明 |
|-----|------|
| `openai` | OpenAI |
| `anthropic` | Anthropic |
| `cohere` | Cohere |
| `vertex_ai` | Google Vertex AI |
| `azure.ai.openai` | Azure OpenAI |
| `aws.bedrock` | AWS Bedrock |
| `_OTHER` | 其他自定义系统 |

---

## gen_ai.operation.name 枚举值

| 值 | 说明 |
|-----|------|
| `chat` | 聊天补全操作（如 OpenAI Chat API） |
| `text_completion` | 文本补全操作（如 OpenAI Completions API Legacy） |
| `embeddings` | 向量嵌入操作 |
| `execute_tool` | 工具执行操作 |
| `invoke_agent` | Agent 调用操作 |
| `create_agent` | Agent 创建操作 |
| `generate_content` | 内容生成操作 |

---

## Agent 相关属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `gen_ai.agent.name` | string | AI Agent 标识符 |
| `gen_ai.agent.description` | string | Agent 角色描述 |

---

## Tool Call 相关属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `gen_ai.tool.name` | string | 外部工具名称 |
| `gen_ai.tool.type` | enum | 工具类型 |
| `gen_ai.tool.arguments` | string | 工具参数（JSON 格式） |

### gen_ai.tool.type 枚举值

| 值 | 说明 |
|-----|------|
| `function` | 函数调用 |
| `web_search` | 网络搜索 |
| `database` | 数据库查询 |
| `retrieval` | 检索操作 |

---

## MCP (Model Context Protocol) 相关属性

OTel v1.39+ 新增了 MCP tracing 属性：

| 属性 | 类型 | 说明 |
|------|------|------|
| `mcp.method.name` | string | MCP 方法名 |
| `mcp.session.id` | string | MCP 会话 ID |
| `mcp.protocol.version` | string | MCP 协议版本 |

---

## Metric 定义

### Metric 名称映射

| OTel Metric 名 | 类型 | 说明 |
|----------------|------|------|
| `gen_ai.client.token.usage` | Counter | Token 消耗量（按 type 分组） |
| `gen_ai.client.operation.duration` | Histogram | 操作耗时 |
| `gen_ai.server.request.duration` | Histogram | 服务端请求耗时 |

### Metric 属性标签

| 属性 | 说明 |
|------|------|
| `gen_ai.system` | LLM 提供商 |
| `gen_ai.request.model` | 模型名 |
| `token.type` | Token 类型（`input` / `output`） |

---

## Span Event（事件）

### 消息事件

用于记录 prompt 和 completion 内容：

```csharp
// 记录输入消息
activity?.AddEvent(new ActivityEvent("gen_ai.input.messages", 
    tags: new ActivityTagsCollection
    {
        { "gen_ai.input.messages", JsonSerializer.Serialize(messages) }
    }));

// 记录输出消息
activity?.AddEvent(new ActivityEvent("gen_ai.output.messages",
    tags: new ActivityTagsCollection
    {
        { "gen_ai.output.messages", JsonSerializer.Serialize(response) }
    }));
```

---

## Span 命名规范

推荐格式：`{gen_ai.operation.name} {gen_ai.request.model}`

示例：
- `chat gpt-4o`
- `embeddings text-embedding-3-small`
- `execute_tool web_search`

---

## 隐私与安全

### 敏感内容处理建议

| 策略 | 说明 |
|------|------|
| **默认** | 仅记录元数据（model、operation、duration、error） |
| **调试模式** | 记录裁剪/脱敏后的消息内容 |
| **生产环境** | 使用白名单和正则表达式清理敏感信息 |

### 配置建议

```csharp
// 选项1：禁用内容捕获（默认）
OTEL_GENAI_CAPTURE_CONTENT=false

// 选项2：在 Collector 层脱敏
// 使用 OTel Collector 的 transform processor

// 选项3：基于 tail 的过滤
// 使用 tail-based sampling 过滤敏感数据
```

---

## 告警指标建议

| 指标 | 告警条件 | 用途 |
|------|---------|------|
| `gen_ai.client.token.usage` rate | > 2x 基线持续 10min | 检测死循环或 prompt 注入 |
| `gen_ai.client.operation.duration` p99 | > 30s | 模型过载或上下文过大 |
| Error rate (span with error status) | > 2% 持续 5min | 限流或配额耗尽 |
| 输入/输出 token 比 | > 10:1 持续 | system prompt 过大 |

---

## Insighta 适配建议

### 当前需要修改的属性映射

| Insighta 当前属性 | OTel GenAI Convention | 修改位置 |
|------------------|----------------------|---------|
| `gen_ai.adapter` | `gen_ai.system` | `TelemetryLlmClient.cs` |
| `llm.stream` | `gen_ai.request.is_stream` | `TelemetryLlmClient.cs` |
| `llm.duration_ms` | `gen_ai.client.operation.duration` | `TelemetryConstants.cs` |
| `tool.name` | `gen_ai.tool.name` | `TelemetryToolCallHandler.cs` |
| `tool.args` | `gen_ai.tool.arguments` | `TelemetryToolCallHandler.cs` |
| `insighta.llm.tokens.*` | `gen_ai.client.token.usage` | `TelemetryConstants.cs` |
| `insighta.llm.request.duration` | `gen_ai.client.operation.duration` | `TelemetryConstants.cs` |

### 需要新增的属性

| 属性 | 说明 | 添加位置 |
|------|------|---------|
| `gen_ai.operation.name` | 操作类型 | `TelemetryLlmClient.cs` |
| `gen_ai.response.finish_reason` | 完成原因 | `TelemetryLlmStream.cs` |
| `mcp.method.name` | MCP 方法名 | `TelemetryToolCallHandler.cs` |
| `mcp.session.id` | MCP 会话 ID | `TelemetryToolCallHandler.cs` |

---

## 参考资源

- [OpenTelemetry GenAI Semantic Conventions 官方文档](https://opentelemetry.io/docs/specs/semconv/gen-ai/)
- [GitHub: semantic-conventions-genai](https://github.com/open-telemetry/semantic-conventions-genai)
- [OpenTelemetry for AI Systems: LLM and Agent Observability (2026)](https://uptrace.dev/blog/opentelemetry-ai-systems)
- [How OpenTelemetry Traces LLM Calls, Agent Reasoning](https://greptime.com/blogs/2026-05-09-opentelemetry-genai-semantic-conventions)

---

*整理人：Insighta*
