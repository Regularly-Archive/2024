# InsightaAI Observability Dashboard Review

> 审查日期：2026-08-14
> 审查人：Insighta（AI Agent，独立复核）
> 审查对象：`tools/observability/grafana/dashboards/` 下 4 个 Grafana dashboard

## 背景

InsightaAI 本地可观测栈（`tools/observability/`，docker-compose：Prometheus + Grafana + Jaeger + otel-collector）已上线。4 个 dashboard 由 Codex 创建，昨日（2026-08-13）日报中记录过一次协作复核（权限拒绝误计、Skill Top 5 改用 max_over_time）。本次为 Insighta 独立复核，用**真实 Prometheus 数据**验证查询语义与代码埋点是否一致。

## 验证基线（Prometheus 实测）

- 观测栈正在运行（prometheus:9090, collector:9464, jaeger:16686, grafana:3000 均在监听）
- Prometheus 中实际存在的指标名（otel-collector 会把 unit 拼进名字）：

```
gen_ai_client_operation_duration_milliseconds_bucket/count/sum
gen_ai_client_tokens_input_total / output_total / cache_hit_total
insighta_agent_round_duration_milliseconds_bucket/count/sum
insighta_agent_round_rounds_total
insighta_agent_run_runs_total
insighta_skill_activation_activations_total
insighta_tool_execution_duration_milliseconds_bucket/count/sum
```

- 各指标有真实数据：runs=23、rounds=201、tool 系列=13、cache_hit=9,220,736 tokens、LLM operation count=203
- **指标名与 4 个 dashboard 的 PromQL 全部匹配，无拼写/标签错误** ✅
- 埋点代码：`src/InsightaAI.Agent.Diagnostics/TelemetryConstants.cs`、`AgentEventTelemetryHook.cs`、`ToolCallHandlerTelemetryWrapper.cs`、`LlmClientTelemetryProxy.cs`

## 发现的问题（按严重程度排序）

### 1. 【语义错误】`input_tokens` 口径不一致，cache 相关指标在 Anthropic 下失真

涉及：`insighta-llm.json` 的 `Uncached input tokens`、`Input : output token ratio`；`insighta-overview.json` 的 `Input cache hit ratio`。

三个查询都假设 `cache_hit ⊆ input`（即 `uncached = input - cache_hit`）：

```promql
round(sum(increase(gen_ai_client_tokens_input_total[$__range]))
    - sum(increase(gen_ai_client_tokens_cache_hit_total[$__range])))   # Uncached input
```

但各家 adapter 的口径不同：

| Provider | input_tokens 是否含 cache_hit | 证据 |
|----------|------------------------------|------|
| OpenAI | **含**（cached_tokens 是 input_tokens 的一部分） | `OpenAIResponseAdapter.cs:587-594` |
| Anthropic | **不含**（cache_read_input_tokens 与 input_tokens 互斥） | `AnthropicAdapter.cs:433-439` |
| Gemini | 待确认 | `GeminiAdapter.cs:364-368` |

影响：
- Anthropic 场景下 `uncached input` 会**低于实际**（input 里本就不含 cache，再减一次变负/偏小）
- `cache hit ratio` 在 Anthropic 下被系统性拉低
- 若混用多 provider，该比值失去意义

建议：按 `gen_ai.system`（或 adapter）分组计算，或统一埋点语义后再做减法；至少在 dashboard 标注口径。

### 2. 【空白风险】Tools Top 5 表用 `$__range` 且默认 1h，低频工具必空白

涉及：`insighta-tools.json` 的 `Top 5 tools by success/failure rate`。

```promql
topk(5, sum by (gen_ai_tool_name) (increase(...[$__range])) / clamp_min(...[$__range], 1)
  and on (gen_ai_tool_name) (sum by (gen_ai_tool_name) (increase(...[$__range])) > 0))
```

- `$__range` 默认 `now-1h`，工具低频时 1h 窗口内 `increase=0`，被 `> 0` 过滤后整表空白
- 这是排行榜，语义上应看累计/较长窗口，而非最近 1h
- 建议：改用 `$__range` 为用户手动拉长，或换成固定长窗口（如 `now-7d`），并加 `min` 提示

### 3. 【语义错误】Skill Top 5 用 `max_over_time` 不等于激活次数

涉及：`insighta-tools.json` 的 `Top 5 most frequently activated skills`。

```promql
topk(5, sum by (insighta_skill_name) (max_over_time(insighta_skill_activation_activations_total[$__range])))
```

- 对单调递增的 counter，`max_over_time` 只返回窗口内最大值，**不等于窗口内激活数**（激活数 = last - first）
- 除非窗口恰好从 counter=0 开始，否则结果恒大于实际激活数；低频 skill 在 1h 窗口内几乎恒为 0，面板大概率显示不全
- 昨日日报记录"Skill Top 5 因此改用 max_over_time"，经本次复核认为**该修正本身不正确**，应改为 `increase(...[$__range])`（同 Tools 面板的激活语义）
- 注：`gen_ai_tool_name` vs `insighta_skill_name` 两处标签写法不统一

### 4. 【小数据量抖动】Agent 面板 `rounds per turn` 除零被 clamp 成假数据

涉及：`insighta-agent.json` 的 `Average rounds per turn`。

```promql
sum(rate(insighta_agent_round_rounds_total[$__rate_interval]))
  / clamp_min(sum(rate(insighta_agent_run_runs_total[$__rate_interval])), 1)
```

- `clamp_min(..., 1)` 在 runs=0 时返回 1，画出一个接近 0 的假值而不是"无数据"
- 刚启动、首 turn 未结束时面板闪假数据
- 建议：用 `... and on() (... > 0)` 语义或直接 `unless` 过滤 0 分母

### 5. 【口径不统一】LLM `Input : output token ratio` 未按模型分组

涉及：`insighta-llm.json` 的 `Input : output token ratio`。

```promql
sum(increase(gen_ai_client_tokens_input_total[$__range]))
  / clamp_min(sum(increase(gen_ai_client_tokens_output_total[$__range])), 1)
```

- 同面板其他图均按 `gen_ai_request_model` 拆分，唯独 ratio 是全局值
- 多模型混跑时该值无参考价值
- 建议：加 `by (gen_ai_request_model)`

### 6. 【NaN 风险】histogram_quantile 的 `sum by (le, ...)` 缺 le 时吞掉 series

涉及：`insighta-agent.json` / `insighta-llm.json` / `insighta-tools.json` 的 latency 图。

```promql
histogram_quantile(0.95, sum by (le, gen_ai_request_model) (rate(..._bucket[$__rate_interval])))
```

- `sum by (le, ...)` 写法正确，但若某 series 的 `le` 标签缺失，`sum by` 会吞掉该值，quantile 返回 NaN
- 目前埋点正常未触发；属防御性风险，可在 bucket 选择时 `{le="+Inf"}` 显式兜底

## 复核确认无误的点（避免重复修改）

- `is_allowed=true` 过滤已正确应用（权限拒绝不计入失败率，`ToolCallHandlerTelemetryWrapper.cs:61-66` 埋点，dashboard 侧过滤）✅
- 指标名、标签名（`gen_ai_tool_name` / `gen_ai_tool_is_error` / `gen_ai_tool_is_allowed` / `gen_ai_request_model` / `agent.id`）与埋点完全一致 ✅
- `insighta-overview.json` 三个 stat（turns / requests / cache ratio）查询可返回真实值 ✅

## 建议的修改清单

| # | 文件 | 面板 | 修改 |
|---|------|------|------|
| 1 | insighta-llm.json | Uncached input tokens | 按 provider 分组或标注口径，避免 Anthropic 下失真 |
| 1 | insighta-overview.json | Input cache hit ratio | 同上 |
| 2 | insighta-tools.json | Top 5 success/failure | `$__range` → 长固定窗口，保留 `> 0` 过滤 |
| 3 | insighta-tools.json | Top 5 skills | `max_over_time` → `increase(...[$__range])` |
| 4 | insighta-agent.json | rounds per turn | 0 分母不画，去掉 clamp_min 假值 |
| 5 | insighta-llm.json | input:output ratio | 加 `by (gen_ai_request_model)` |
| 6 | 各 latency 图 | - | 显式 `{le="+Inf"}` 兜底（可选，防御性） |

## 验证方式

- 修改后 `docker compose restart prometheus grafana` 或仅重载 dashboard（Grafana provisioning 会热加载）
- 用 `curl http://localhost:9090/api/v1/query?query=<promql>` 逐一验证改后的 PromQL 返回非空
- 对照本文档"验证基线"中的真实指标名与数据量
