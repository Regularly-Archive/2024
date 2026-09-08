# LLM 思考强度控制机制调研

> 来源：researcher 子代理调研（2026-08-27），经 Insighta 归档整理
> 2026-09-08 复核：本文保留历史调研，不能直接作为协议规范。Chat Completions 使用顶层 reasoning_effort，Responses 使用 reasoning.effort；o 系列不能据此认定支持 none。GPT-5 默认 medium，GPT-5.1/5.2 基础模型默认 none。Gemini 预算 0 不能推广至所有模型。当前保守策略和官方来源见 [交接手册顶部修订](../architecture/llm-reasoning-control-handoff.md)。其它未经逐项复核的版本、计费和框架描述仍需验证。
> 消费方：`docs/architecture/llm-reasoning-control-design.md`

# LLM API"思考强度"控制机制调研

调研时间：2026-08-27。以下结论均基于各供应商公开文档与社区验证信息；因 o 系列 / GPT-5 系列仍在快速迭代，标注了"当前"的时间点。

---

## 1. OpenAI

### 1.1 两个 API 的差异

OpenAI 目前有两条调用路径，控制方式不同：

- **Chat Completions API**（传统路径，`/v1/chat/completions`）：早期用顶层扁平参数 `reasoning_effort`。近期已被改为嵌套对象 `reasoning: { effort: "..." }`。对 GPT-5 系列，`reasoning.effort` 与 `verbosity` 在 Responses API 支持更完整；Chat Completions 的某些 GPT-5 变体（如 `gpt-5-chat-latest`、`gpt-5-search-api`）会拒绝顶层 `reasoning_effort`，提示该参数仅在 Responses 中以特定形式支持。[langchain4j bug #4898](https://github.com/langchain4j/langchain4j/issues/4898)、[OpenAI 社区讨论](https://community.openai.com/t/evals-invalid-reasoning-effort-for-non-reasoning-model-gpt-5-chat-latest/1341785)

- **Responses API**（新路径，`/v1/responses`）：通过 `reasoning: { effort: "...", summary: "..." }` 控制。这是当前推荐路径，尤其对 GPT-5 系列。[社区讨论](https://community.openai.com/t/reasoning-no-longer-available-in-api-responses/1116490)

### 1.2 当前取值（2025-12 前后）

枚举范围已从早期的 `{low, medium, high}` 扩展到：`none`、`minimal`、`low`、`medium`、`high`、`xhigh`、`max`。各模型支持情况差异较大（来源：[社区整理的兼容性矩阵](https://community.openai.com/t/request-for-compatibility-matrix-reasoning-effort-sampling-parameters-across-gpt-5-series/1371738)）：

| 模型 | none | minimal | low | medium | high | xhigh |
|---|---|---|---|---|---|---|
| gpt-5.1-2025-11-13 | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ |
| gpt-5.2-2025-12-11 | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| gpt-5.2-pro-2025-12-11 | — | — | — | ✓ | ✓ | ✓ |
| gpt-5.2-chat-latest | — | — | — | ✓ | — | — |
| gpt-5-pro-2025-10-06 | — | — | ✓ | ✓ | ✓ | — |
| gpt-5.1-chat-latest | — | — | — | ✓ | — | — |
| gpt-5-search-api-2025-10-14 | — | — | — | — | — | — |
| o3-mini / o1 系 | — | — | ✓ | ✓ | ✓ | — |

**要点**：
- `minimal` 是新近出现的等级，语义上介于 `none` 和 `low` 之间，社区有帖子专门讨论"effort: minimal 是否比 low 更低"。[社区](https://community.openai.com/t/is-03-mini-in-the-api-the-low-medium-or-high-version/1110423)
- `xhigh` / `max` 是高天花板等级，仅最新 5.1/5.2 系支持。
- `gpt-5-chat-latest` 和 `gpt-5-search-api` 等变体**不支持** reasoning（会返回 invalid_request_error），属于非推理模型。[Azure 文档](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/reasoning)、[OpenAI 社区](https://community.openai.com/t/evals-invalid-reasoning-effort-for-non-reasoning-model-gpt-5-chat-latest/1341785)

### 1.3 语义

- **o 系列**（o1、o1-mini、o3、o3-mini、o4-mini）：默认就带 reasoning，`reasoning_effort` 是可选调节。o3-mini 支持 `low/medium/high`。[Azure 文档](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/reasoning)
- **GPT-5 / GPT-5.1+**：默认**不**推理。必须显式设置 `reasoning.effort`（不能是 `none`）才能触发 thinking。这与 o 系列行为相反，是一个关键陷阱。[qwe.edu.pl 教程](https://www.qwe.edu.pl/tutorial/reasoning-effort-levels-guide)

### 1.4 Reasoning Summary 的获取

- 在 Responses API 中，通过请求体 `reasoning.summary` 参数控制返回粒度，取值为 `"concise"` / `"detailed"`（早期也接受 `"none"`）。
- 响应体里 `output` 数组会有 `type: "reasoning"` 的 item，其 `summary` 字段是文本摘要数组。
- 已知行为：即使设了 `"detailed"`，o3 模型在部分请求中仍可能返回空 `summary` 数组（[社区 40 条回复的 bug 报告](https://community.openai.com/t/o3-model-in-api-often-omits-reasoning-summary-despite-reasoning-summary-detailed/1307301)）。
- `effort: "minimal"` 似乎会抑制 summary 输出（[社区讨论](https://community.openai.com/t/does-setting-reasoning-effort-minimal-suppress-reasoning-summaries/1323058)）。
- Agent SDK 中可通过 `result.raw_responses[].output[].type == "reasoning"` 与 `result.new_items[].type == "reasoning_item"` 两种路径遍历读取 summary。[社区](https://community.openai.com/t/how-to-get-reasoning-summary-using-gpt-5-mini-in-agent-sdk/1358227)

---

## 2. Anthropic Claude

Anthropic 的设计最复杂，经历了"手动预算 → 自适应努力度"的演进。

### 2.1 两种模式

**A. 手动模式（manual）：`thinking: { type: "enabled", budget_tokens: N }`**
- `budget_tokens` **最小值 1024**，API 拒绝更小的值。[Claude Platform Docs](https://docs.claude.com/en/docs/build-with-claude/extended-thinking)
- `budget_tokens` 必须 < `max_tokens`（因为 thinking token 计入当轮 `max_tokens`）。**例外**：interleaved thinking 模式下 `budget_tokens` 可以 > `max_tokens`，因为预算覆盖的是整个 assistant turn 内的所有 thinking block。[同](https://docs.claude.com/en/docs/build-with-claude/extended-thinking)
- `budget_tokens` 是**目标值（target）**，不是硬上限；实际消耗可随任务浮动。

**B. 自适应模式（adaptive）：`thinking: { type: "adaptive" }` + `output_config: { effort: "low|medium|high|xhigh|max" }`**
- 从 Opus 4.7（2026-04-16 发布）开始引入。**不再有 `budget_tokens`**，改用 `effort` 软引导。
- 模型自主决定是否思考、思考多少；低 effort 时简单任务可能完全跳过思考。
- `effort` 语义：软引导比例分配，不保证 token 数量。[Claude Steering thinking 文档](https://platform.claude.com/docs/en/build-with-claude/thinking-steering-and-cost)

### 2.2 建议起点

- 简单任务：从 1024 起步逐步增加
- 复杂任务：从 16000+ 起步
- 高预算有边际递减。[Claude Platform Docs](https://platform.claude.com/docs/en/build-with-claude/extended-thinking)

### 2.3 Interleaved thinking 与 tool use

- **Interleaved thinking（Beta，`anthropic-beta: interleaved-thinking-2025-05-14`）**：允许 thinking block 出现在多个 tool call 之间（而不仅是回答之前）。这让 agent 在长 tool-call 链中能对中间结果再思考。[同](https://platform.claude.com/docs/en/build-with-claude/extended-thinking)
- **与 "think" tool 的区别**：`think` tool 是"开始回答后"再停下来思考（适合复杂 tool-call 链中的决策节点）；extended thinking 是"回答前"先想完再行动。2025-12-15 起 Anthropic 官方建议在大多数场景用 extended thinking 替代 think tool。[Anthropic Engineering](https://www.anthropic.com/engineering/claude-think-tool)

### 2.4 思考 token 是否进入下一轮上下文

**是，必须回传**。在多轮 tool-use 循环中：
- 上一轮 thinking block 会被**自动缓存**（无需显式 `cache_control`），并作为 input tokens 计费。
- 如果中途禁用 thinking 并把 thinking 内容作为当前 turn 的 tool-use 上下文传入，thinking 内容会被剥离（graceful degradation）。
- **Cache 失效规则**：改变 thinking 参数（开/关、改预算）会失效化 cache breakpoint。interleaved thinking 进一步放大失效影响。
- 官方建议：**把历史 thinking block 全量回传**，API 会自动过滤，只保留维持推理连续性所需的 block；只对被模型实际"看到"的 block 计费。可通过 `clear_thinking_20251015`（context-editing strategy，beta header `context-management-2025-06-27`）自定义清理策略。[Thinking 文档](https://platform.claude.com/docs/en/build-with-claude/thinking)、[Bedrock 文档](https://docs.aws.amazon.com/bedrock/latest/userguide/claude-messages-extended-thinking.html)

> **对 agent 框架的关键启示**：你的抽象层必须在多轮对话中**透传** thinking block；否则模型会"忘记"上一次的推理状态，连续性丢失。

### 2.5 计费口径

- thinking tokens **算作 output tokens**，按标准 output 价格计费，没有单独计价档。
- 响应 `usage.output_tokens_details.thinking_tokens` 给出 breakdown；`output_tokens` 仍是计费权威的总数。
- 流式时该 breakdown 只在最后的 `message_delta` 事件出现。[Steering thinking 文档](https://platform.claude.com/docs/en/build-with-claude/thinking-steering-and-cost)

---

## 3. Google Gemini

### 3.1 `thinkingBudget` 语义

配置路径：`GenerateContentConfig.thinking_config.thinking_budget`。[Google 开发者博客](https://developers.googleblog.com/gemini-api-io-updates)

| 值 | 语义 |
|---|---|
| `0` | 关闭思考 |
| `-1` | **dynamic**，模型按任务复杂度自动调节 |
| 正数 N | 固定预算，最多 N 个思考 token |

官方示例：`thinking_budget: 1024` 配合 `include_thoughts: True`。

### 3.2 `includeThoughts`

- `includeThoughts: True` 时，响应 `candidates.content.parts` 中会出现 `part.thought` 字段，携带原始 thinking 文本。
- Google 同时提供 **Thought Summaries**（2.5 Pro 与 2.5 Flash 均已上线）：把原始 CoT 合成带标题、工具调用的可读摘要，在 Google AI Studio 中展示。[Google 开发者博客](https://developers.googleblog.com/gemini-api-io-updates)

### 3.3 2.5 Flash vs Pro vs Flash-Lite

- **2.5 Flash**：原生 thinking 预算 + dynamic 推理，主打高并发/成本敏感场景。2025-09-20 preview 版本推理模式 AAII 得分 54，比前一版本提升 3 分。[VentureBeat](https://venturebeat.com/technology/googles-gemini-2-5-flash-lite-is-now-the-fastest-proprietary-model-and)
- **2.5 Pro**：能力更强，支持 **"Deep Think"** 实验模式（针对复杂数学/代码），官方在 2025 I/O 宣布。2.5 Pro 推理预算能力稍后对开发者开放。
- **2.5 Flash-Lite**：速度最快的自有模型；推理模式 AAII 得分 48（较前一版本 +8），非推理 42（+12）。Live API 后续也将加入 thinking 能力。
- 预算上限：2025 年 4 月公布时，thinkingBudget 范围为 0 – 24576 tokens。[artificialintelligence-news.com](https://www.artificialintelligence-news.com/news/google-introduces-ai-reasoning-control-gemini-2-5-flash)

---

## 4. 国内模型与 OpenAI 兼容端点

这些端点表面共享 Chat Completions 请求格式，但思考控制字段并不兼容。不能仅因某个模型名使用 OpenAI 兼容 API，就假定它接受 `reasoning_effort`。

### 4.1 Qwen

- DashScope 的混合思考模型使用 `enable_thinking: true|false`；未传时的默认值随模型而变。[DashScope 文档](https://help.aliyun.com/en/model-studio/qwen-api-via-dashscope)
- 较新的 OpenAI Responses 兼容接口还提供 `reasoning.effort`；它优先于旧的 `enable_thinking`，并支持 `none` 到 `max` 的档位。两套字段不能被当作长期稳定的同义词。[OpenAI Responses 兼容文档](https://help.aliyun.com/zh/model-studio/qwen-api-via-openai-responses)
- 因而通用层的 `Off` 可以映射为已知兼容模型的 `enable_thinking: false`，但模型能力表必须决定具体版本是否可发送该字段。

### 4.2 GLM

- GLM OpenAI 兼容端点支持 `thinking.type: "enabled" | "disabled"`；GLM 5.2 及以上还公开了 `reasoning_effort` 档位。[GLM 思考能力文档](https://docs.bigmodel.cn/cn/guide/capabilities/thinking)
- 其 effort 集合与统一集合不完全一致：官方说明 `low` / `medium` 会落到较高思考强度，`xhigh` 对应最高档。因此适配器需要映射，而不是直接透传所有档位。

### 4.3 豆包、MiniMax 与 DeepSeek

- 豆包的思考控制以模型版本为单位开放，公开 API 采用 `thinking.type` 的启用、关闭或自动语义；不能把一个版本的字段支持外推到全部 Seed 模型。
- MiniMax M3 支持 `thinking: { type: "disabled" | "adaptive" }`，默认开启；M2.x 即使收到 disabled 也不会真正停止思考。`reasoning_split` 仅改变输出呈现，不是关闭开关。[MiniMax OpenAI API 文档](https://platform.minimax.io/docs/api-reference/text-openai-api)
- DeepSeek 的思考模式同样以 `thinking.type` 控制，但能力范围和历史 reasoning 的回传要求取决于具体模型版本。[DeepSeek 思考模式文档](https://api-docs.deepseek.com/zh-cn/guides/thinking_mode/)

**结论**：`Off` 的正确语义是“若已知该模型支持关闭，则显式关闭”；未知或确认不支持时，宁可不发送控制字段并报告降级，也不能伪造“已关闭”。这要求后续的能力矩阵同时描述字段、版本和实际应用结果。

---

## 5. 业界 Agent 框架设计

### 5.1 Claude Code

- **UI 切换**：通过 Effort 菜单的 Thinking toggle 开关；在 Claude Web/App 里"Extended"开关。
- **关键词触发（legacy）**：v1 时代有 `think`、`think hard`、`ultrathink` 三级关键词，用户输入即可切换；当前版本简化为"Thinking On/Off"。[Reddit 讨论](https://www.reddit.com/r/ClaudeCode/comments/1nyly8m/what_is_the_difference_between_thinking_onoff_and)
- **无法在所有 effort 下关闭**：在 Opus 5 上 extended thinking 无法关闭；API 上 effort `high` 及以下可关闭，`xhigh`/`max` 下尝试关闭会报错。[Anthropic Support](https://support.claude.com/en/articles/8664678-change-the-model-effort-and-thinking-settings)
- **自动调节策略**：无公开算法。策略层面的公开建议是"简单任务 OFF、复杂推理/代码 ON、创作/检索 OFF"。[GitHub gist](https://gist.github.com/intellectronica/58571dda3581eec3e17a77741e8c858a)

### 5.2 Codex CLI（OpenAI）

- **启动时全局配置**：`codex --model gpt-5.6`，`codex -c model_reasoning_effort=high`。
- **运行中切换**：`/model` 斜杠命令可切换模型 + reasoning effort；`alt+,` / `alt+.` 也可在 TUI 中实时增减。[Codex docs](https://learn.chatgpt.com/docs/models)、[GitHub issue #27457](https://github.com/openai/codex/issues/27457)
- **层级**：内置 Power/Smarter/Faster 三档（底层映射到 Sol/Terra/Luna 模型 + 默认 effort）。默认 Power = `gpt-5.6-sol` + medium。
- **自动调节**：无公开策略，纯用户驱动。[kingy.ai 分析](https://kingy.ai/news/openai-codex-reasoning-levels-low-medium-high-extra-high)

### 5.3 OpenCode CLI

- **顶层 `--variant` flag**：`--variant high|max|minimal`，是 provider-specific 的 reasoning effort。[OpenCode CLI docs](https://opencode.ai/docs/cli)、[cheat sheet](https://computingforgeeks.com/opencode-cli-cheat-sheet)
- **内置变体**：Anthropic 提供 `high`（默认）/ `max`；OpenAI 提供更多级。用户可定义 custom variants。[OpenCode models docs](https://opencode.ai/docs/models)
- **`--thinking` flag**：控制 TUI 是否显示 thinking block。
- **设计亮点**：把 reasoning 视为"同一模型的不同变体"，用 variant 系统管理，天然支持按 provider 差异化。

### 5.4 LangChain / LangGraph

- LangChain 把 `reasoning_effort` 暴露为 `BaseChatOpenAI.reasoning_effort`，取值 `minimal/low/medium/high`。[LangChain Reference](https://reference.langchain.com/python/langchain-openai/chat_models/base/BaseChatOpenAI/reasoning_effort)
- **无跨供应商统一抽象**：每个 provider 走各自的 providerOptions。LangGraph 同样没有统一的 thinking control 层。
- **已知坑**：langchain4j 还在用老的顶层 `reasoning_effort` 序列化，会被新 GPT-5 模型拒绝（[issue #4898](https://github.com/langchain4j/langchain4j/issues/4898)）。

### 5.5 Vercel AI SDK（最重要的统一抽象先例）

Vercel AI SDK Core 提供了**跨供应商的统一顶层 `reasoning` 参数**（详见下节）。这是目前公开可参照的、最接近"统一抽象层"的实现。[AI SDK Core Docs](https://ai-sdk.dev/docs/ai-sdk-core/reasoning)

---

## 6. 统一抽象的参考

### 6.1 Vercel AI SDK Core —— `reasoning` 顶层参数

这是目前**业界最明确的跨供应商统一思考控制先例**。
- **取值**：'provider-default'、'none'、'minimal'、'low'、'medium'、'high'。
- **设计**：统一参数表达"思考多少"的意图，各 provider 翻译为自身机制（OpenAI effort / Anthropic thinking budget / Gemini thinkingBudget）；无法翻译或需要精确控制的场景走 `providerOptions.reasoning` 兜底。
- **启示**：这是本框架统一抽象的直接参照——档位为公共语义，机制差异下沉到适配器。

### 6.2 Microsoft.Extensions.AI

- `ChatOptions` 无 reasoning effort / thinking budget 的标准字段（截至 2026-08），各实现方自行扩展。无先例可借鉴。

---

## 7. 对统一抽象层设计的启示

**公共维度**（可统一）：

1. **意图档位**：`provider-default / none / minimal / low / medium / high` 是三家可交付的公共子集（Vercel 已验证）。
2. **"不传参数"语义**：三家的默认行为相反（o 系列推理 / GPT-5 不推理 / Claude 不思考 / Gemini 动态思考），"未表达意图"必须是显式语义位，与"明确关闭"区分。
3. **思考 token 计量拆分**：三家都提供 breakdown（OpenAI reasoning tokens / Anthropic `thinking_tokens` / Gemini `thoughtsTokenCount`），可统一为 `ReasoningTokens`。

**不可调和的差异**（留在适配器）：

1. **机制形态**：档位（OpenAI）vs 预算（Anthropic manual / Gemini）——用映射表翻译，映射结果按各家边界 clamp。
2. **预算语义**：Anthropic budget 是目标值且计入 max_tokens、interleaved 模式可超；Gemini 是硬预算且有 `0/-1` 特殊值。
3. **思考内容回传**：Anthropic 必须回传 thinking block 保连续性；OpenAI Responses 用 reasoning item；Gemini `includeThoughts` 可选。
4. **动态调整成本**：Anthropic 改 thinking 参数破坏 prompt cache——档位应为会话级配置而非每轮调整。
5. **档位集合不齐**：`xhigh/max` 仅部分模型支持，需要能力声明 + 降级策略。

（注：本节由 Insighta 依据正文内容补全，子代理原始输出在此处因长度上限截断。）
