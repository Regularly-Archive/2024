# LLM 输出截断处理调研

> 日期：2026-09-30
> 动机：researcher 子 Agent 生成 400 条长文的实测事故（详见下文"实测案例"），以及 InsightaAI 截断续写机制的设计验证需求。

## 1. 问题定义

LLM 输出被 `max_tokens` 截断时的 finish/stop reason：

- OpenAI：`finish_reason = "length"`
- Anthropic：`stop_reason = "max_tokens"`（另有 `model_context_window_exceeded`）
- InsightaAI：`DoneReason.MaxTokens`

截断的特殊性：**它是"成功但未完成"，不是失败**。响应是合法的 HTTP 200，内容不完整但不报错。处理不当会产生静默截断——调用方拿到半截结果却以为任务完成。

## 2. 业界光谱总览

| 框架/产品 | 处理方式 | 自动续写？ |
|-----------|----------|-----------|
| Anthropic 官方 | stop_reason 决策表 + 官方续写模式（见 §3） | 给出模式，调用方实现 |
| Pi（coding agent） | length/context overflow stop → **one compact-and-retry**（压缩后重试，上限一次，并防级联压缩） | ✅ 唯一全自动 |
| n8n | 检测 finish_reason → 追加 `Continue from where you left off: [尾部 200 字符]` | ✅ 教程级 |
| hermes-agent（NousResearch） | 曾因对短而完整的响应触发虚假续写返修（见 §4.1） | ✅（带守卫） |
| LangChain / LangGraph | **完全不管输出侧**。truncation 策略全部针对输入侧（对话历史管理）；输出截断只透传 response metadata | ❌ |
| Vercel AI SDK | 暴露 `finishReason: 'length'`，由调用方决定 | ❌ |
| agno | 自动续写仍是社区 feature request，未内置 | ❌ |

**结论**：自动续写在主流框架中并不普遍——LangChain / Vercel / agno 都不做，Pi 的 compact-and-retry 是唯一接近的自动机制（但思路是压缩输入释放输出空间，适用于上下文挤占场景，与续写不同）。InsightaAI 的续写实现比主流框架走得更远。

### 4.1 反面教材：虚假续写与无进展续写（hermes-agent #58087）

[NousResearch/hermes-agent #58087](https://github.com/NousResearch/hermes-agent/issues/58087) 报告的 bug 对我们直接相关：GLM / Ollama（OpenAI 兼容层）这类 provider 会在**模型自然停止但 token 数恰好贴近 `max_tokens`** 时也上报 `finish_reason="length"`——内容短而完整却被判定为截断。后果：

1. **虚假续写**：对完整响应发起续写，产出重复内容或"以上就是全部"式的废话，污染合并文本
2. **无进展循环**：续写返回相同内容且再次报 length，循环空转到上限

社区给出的守卫建议，均值得采纳：

- 内容短且完整（正常标点收尾、无句中断）→ 不触发续写，或设最小内容长度阈值
- **no-progress 检测**：续写后内容没有增长 → 立即停止循环，不要烧到上限

InsightaAI 当前续写循环由 `FinishReason == MaxTokens` 单一条件驱动，需要补 no-progress 守卫（见 §6 行动项）。

## 3. Anthropic 官方决策表

来源：[Stop reasons and fallback](https://docs.anthropic.com/en/docs/build-with-claude/handling-stop-reasons)

| stop_reason | 官方处理 |
|-------------|----------|
| `end_turn` | 直接使用。**空响应陷阱**：偶发 2–3 token 空内容 + `end_turn`，禁止原样重试（无新信息不会改变结果）；先修消息结构（常见原因：tool result 后紧跟 text 块，教会模型等待用户输入），continuation prompt 是最后手段 |
| `max_tokens` | **首选 raise `max_tokens`，其次 continue the response**。官方续写模式：`[user(任务), assistant(已有内容), user("Please continue from where you left off.")]` |
| 截断块含**工具调用** | **必须提高 max_tokens 重试，不能续写**（残缺 JSON/结构化块无法续） |
| `pause_turn` | server-tools 循环的官方续写机制：assistant 内容原样回填 messages + 再发请求 |
| `model_context_window_exceeded` | 视为截断处理；也可用于"不知道输入多大时请求最大输出" |
| `refusal` | 读 `stop_details`，fallback 模型重试 |

Streaming：`stop_reason` 只在 `message_delta` 事件中出现（`message_start` 里为 null）。

### 空响应的三级策略（官方顺序）

1. 修消息结构（消除模型学会的坏模式）
2. 修不好再考虑 continuation prompt（改变上下文）
3. **绝不原样重试**——相同输入期望不同输出不是重试，是重复失败

## 4. 实践经验（digitalapplied 案例）

来源：[When Your AI Agent Quietly Gets a Half-Finished Answer](https://digitalapplied.io/when-your-ai-agent-quietly-gets-a-half-finished-answer/)

- **按输出形状定制策略**：散文/正文截断可续写；工具调用块截断必须重试或提高 max_tokens。一个 Agent 里两种形状要区别对待
- **stop_reason 必须进 instrumentation**：案例中 vLLM 流式端点在发出 tool-call delta 后把 terminal finish_reason 无条件改写为 `tool_calls`，截断原因静默丢失，三个月内三个 PR 才修完。审计清单：每类模型调用的失败模式盘点、stop_reason + token usage 打点、网关 finish_reason 保真检查、框架隐式 ceiling 排查
- 截断的表现是"Agent 静默交付半成品"——用户感知不到，只能靠遥测发现

## 5. 实测案例（InsightaAI，2026-09-30）

会话 `2dd7367a34e9`，researcher 子 Agent（glm / bigmodel Anthropic 端点，`MaxTokens=8192`）被要求一次生成 400 条城市饮食介绍（约 3 万字）：

| 阶段 | 现象 |
|------|------|
| 生成中 | 模型先 thinking，8192 预算被思考烧光，**正文零字**；空文本守卫（commit `ce947bc`）同历史重调，模型行为近乎确定，连续 9–15 轮 thinking-only |
| 15 轮耗尽 | max-rounds 兜底一次性调用（Temperature=0）写出 27 条后撞限，**句中截断**，状态 `Completed` |
| 代价 | 45 分钟、outputTokens=131,072（= 16 次调用 × 8192），有效产出 27 条 |

**根因分层**：

1. **触发器**：thinking 无独立预算控制，烧光输出预算 → 截断退化为"空转"（无正文可续写）
2. **根因 1（策略）**：空文本守卫的裸 `continue` = 相同输入重复失败。改变行为必须改变上下文（注入续写指令），原样重调永远不会变好
3. **根因 2（配置）**：3 万字任务配 8192 `max_tokens`，输出需求与预算严重错配；即便零思考也要撞限 6 次

对照官方决策表：裸 `continue` 连"空响应三级策略"的第一级（修结构）都算不上，是第四级的无效重试。

## 6. InsightaAI 现状对照

### 已做对（有官方背书）

| 做法 | 官方依据 |
|------|----------|
| 续写投影：assistant(累积草稿) + user(续写指令模板 `continue-after-truncation.txt`)，不污染真实历史 | 与官方 "Ensuring complete responses" 模式一致 |
| 工具调用截断不续写（保持现状：执行报错 → 模型自愈） | 官方：残缺工具块必须重试，不能续 |
| 流式 `DoneEvent.Reason` 全程携带 finish_reason | 官方 streaming 指导（stop_reason 在 message_delta） |
| 续写轮不带工具（`Tools = []`） | 官方续写请求同样不携带 |
| `AgentResult.WasTruncated` 标记 | "stop_reason 必须可观测"的实践 |

### 缺口与行动项

| # | 事项 | 对应发现 |
|---|------|----------|
| 1 | **空文本 + MaxTokens 改为注入指令后重跑**：向模型注入"跳过思考，直接输出正文"类指令（改变上下文），上限 1–2 次；替代空文本守卫的裸 `continue`。原则：**重试必须携带新信息，原样重跑 = 重放失败** | §3 空响应三级策略、§5 根因 1 |
| 2 | **子 Agent / 长文任务支持配置更大 `MaxTokens`**；续写是兜底，raise max_tokens 才是首选 | §3 官方排序 |
| 3 | **截断/续写喂 telemetry**：截断次数、续写次数、兜底截断率进 Agent Dashboard | §4 instrumentation |
| 4 | 映射 Anthropic `model_context_window_exceeded`（当前未处理，应归入 `MaxTokens` 或单独 DoneReason） | §3 决策表 |
| 5 | 核对投影/历史消息构造：tool result 后不紧跟 text 块（避免教出空响应习惯） | §3 空响应成因 |
| 6 | 兜底（reached-max-rounds）收尾调用撞限时的处理：目前只标 `WasTruncated` 不续写——接受或补续写，需明确决策 | §5 实测 27 条截断 |
| 7 | "按输出形状定制策略"矩阵写进设计文档（正文→续写 / 工具块→重试） | §4 |
| 8 | **续写循环加 no-progress 守卫**：每轮续写后比较草稿长度，零增长立即停止（不烧到 MaxContinuations 上限）。一个守卫同时兜住两种病态：GLM/Ollama 误报 length 产废话、续写轮 thinking 烧光预算无正文 | §4.1 |
| 9 | **长文生成类子 Agent 任务默认 `ReasoningPreference.Off`**：从源头不让思考挤占输出预算（`ModelReasoningResolver` 基础设施已在，差子 Agent 配置链路接入） | §5 根因 1 的根治 |

### 优先级顺序（2026-09-30 讨论确定，暂缓执行）

1. **空文本守卫改造 + no-progress 守卫**（#1 + #8）：改动集中在 `AgentLoop.cs`（完成分支 + `RunContinuationAsync`），一并实现并补单测
2. **提交现有改动**：截断续写实现（未提交，5 个文件）+ 本文档——先落盘再动工，避免再被后续改动覆盖
3. **重启进程实测**：VS 调试的长驻进程从 11:3x 活到 15:35，跑的一直是 ce947bc 旧代码；需重启加载新构建后，用机械抄写 400 条压测（零思考，精准命中正文截断续写路径）
4. telemetry 打点（#3）与 reasoning off 接入（#9）可并行推进

## 7. 参考链接

- [Anthropic — Stop reasons and fallback](https://docs.anthropic.com/en/docs/build-with-claude/handling-stop-reasons)
- [digitalapplied — When Your AI Agent Quietly Gets a Half-Finished Answer](https://digitalapplied.io/when-your-ai-agent-quietly-gets-a-half-finished-answer/)
- [Pi — badlogic/pi-mono（compact-and-retry：overflow recovery 上限一次并防级联）](https://github.com/badlogic/pi-mono)
- [NousResearch/hermes-agent #58087 — spurious continuation on short complete responses](https://github.com/NousResearch/hermes-agent/issues/58087)
- n8n 教程（finish_reason 检测 + continue prompt）：n8n blog，未存档具体 URL
