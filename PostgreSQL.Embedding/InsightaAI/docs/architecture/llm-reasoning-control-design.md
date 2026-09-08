# LLM 推理强度控制设计

> 状态：2026-09-08，Off 收尾；当前实现以 [交接手册顶部修订](llm-reasoning-control-handoff.md) 为准。下文 8 月映射表为历史方案，特别是 o 系 none、全 Gemini budget=0 和家族前缀推断不可作为实现依据。Anthropic budget/max_tokens 策略仍待决。
> 分支：`feature/llm-thinking-control`
> 调研报告：`docs/references/llm-thinking-control-survey.md`（researcher 子代理产出，见会话 artifact）

## 1. 背景

主流 LLM API 均已提供"思考强度"控制，但形态互不相同且仍在快速演进：OpenAI 赌档位（`reasoning.effort`），Anthropic 从预算（`budget_tokens`）转向自适应档位（Opus 4.7+ `adaptive effort`），Google 坚持预算（`thinkingBudget`，`0=关闭 / -1=动态 / 正数=固定`）。本设计为 InsightaAI 的 LLM 抽象层提供统一的思考强度控制语义。

**核心判断**：统一的是**意图**（想多深），不是**机制**（每家怎么实现）。差异不可调和的部分关进适配器。混乱是竞争期的正常状态（function calling 的前车之鉴），抽象层的价值恰恰在于吸收这种变化——供应商改机制，我们只改一个适配器。

## 2. 现状盘点（2026-08-27）

已有基础：

- `LlmRequest.Reasoning: ReasoningConfig?`（`Enabled / BudgetTokens? / Effort?`）——统一入口已存在
- `IProviderAdapter.SupportsReasoning / SupportedReasoningModes`（flags 枚举）
- Anthropic（manual budget + thinking 流）、OpenAI CC（`reasoning_effort`）、OpenAI Responses（`reasoning.effort` + reasoning item 回传）均已接入
- `ThinkingBlock` 有持久化与回传（Anthropic 多轮连续性依赖它）

问题：

| 问题 | 位置 |
|------|------|
| `ReasoningEffort` 枚举仅 `Low/Medium/High` 三档，落后于 OpenAI（`none~max` 七档）与 Anthropic adaptive（`low~max`） | `src/InsightaAI.LLM/Models/LlmRequest.cs` |
| `Enabled=true` 且 Effort/Budget 均空时行为靠隐式默认（Anthropic 回退 10000，OpenAI 回退 medium） | 各适配器 |
| 模型能力硬编码为前缀数组，散落在适配器内 | `OpenAIAdapter.cs`、`AnthropicAdapter.cs` |
| Gemini 完全未接入 thinking | `GeminiAdapter.cs` |
| Agent 层无 Reasoning 配置通道，仅 `SummaryService` 硬编码 `Enabled=false`；CLI 无任何开关 | `SummaryService.cs` |

## 3. 业界结论摘要

### 3.1 三家机制

| 维度 | OpenAI | Anthropic | Gemini |
|------|--------|-----------|--------|
| 形态 | 档位（Responses API `reasoning.effort`） | 预算（manual `budget_tokens`）→ 档位（adaptive `effort`，Opus 4.7+） | 预算（`thinkingBudget`） |
| 取值 | `none/minimal/low/medium/high/xhigh/max`，各模型支持矩阵不一 | budget ≥ 1024 且计入 max_tokens；effort 为软引导 | `0`（关闭）/ `-1`（动态）/ 正数（固定，上限约 24576） |
| 不传参数的默认行为 | o 系列默认推理；GPT-5 默认**不**推理 | 默认不思考 | 2.5 Flash 默认动态思考 |
| 思考内容回传 | Responses API reasoning item | **必须回传** thinking block 以保持连续性 | `includeThoughts` 可选返回 |
| 动态调整代价 | — | **改 thinking 参数破坏 prompt cache** | — |
| 计费 | 按 output 计费 | thinking token 按 output 计费，`usage.thinking_tokens` 拆分 | `thoughtsTokenCount` 拆分 |

### 3.2 业界框架做法

- **Vercel AI SDK**：唯一成熟的跨供应商统一 `reasoning` 顶层参数（`provider-default/none/minimal/low/medium/high`），特异需求走 `providerOptions` 兜底——本设计直接借鉴此结构。
- **Codex CLI**：全局配置 + `/model` 命令 + 快捷键运行时切换；默认显式 medium（跨模型行为可预测，牺牲默认成本）。
- **OpenCode**：将 reasoning 视为同一模型的不同 variant。
- **Claude Code**：Thinking On/Off 开关；无自动调节策略。
- **共同点**：没有任何框架公开自动调节策略，全部用户驱动。

## 4. 设计原则

1. **档位表达意图，预算负责落地**。用户、Agent 配置、CLI 切换全部使用档位；预算是预算型模型的实现细节，由适配器内映射表翻译。
2. **`ProviderDefault` 是显式语义位**："用户未表达意图"本身是一种意图，且不传参数在各家默认行为相反（o 系列推理 / GPT-5 不推理 / Claude 不思考 / Gemini 动态思考），必须与 `Off` 区分。
3. **`Off` 是明确关闭**：需要显式传参的模型必须传（Gemini `thinkingBudget=0`）。
4. **抽象泄漏限制在翻译层**：不可调和的差异（cache 失效、计费口径、budget 语义）不进入统一语义。
5. **档位是会话级配置**，不做每轮自动调节——Anthropic 改 thinking 参数破坏 cache，频繁调整有真实成本；业界亦无自动调节先例，只预留接口。

## 5. 统一语义模型（M1）

### 5.1 ReasoningConfig v2

```csharp
public enum ReasoningControl
{
    Off,              // 明确关闭（Gemini: thinkingBudget=0; Anthropic: 不传 thinking）
    ProviderDefault,  // 不传任何思考参数，供应商默认行为；配置缺省值
    Effort,           // 档位驱动
    Budget            // 原生预算直通（ProviderOptions 逃生舱场景）
}

public enum ReasoningEffortLevel
{
    None, Minimal, Low, Medium, High, XHigh
}

public sealed record ReasoningConfig
{
    public ReasoningControl Control { get; init; } = ReasoningControl.ProviderDefault;
    public ReasoningEffortLevel? Effort { get; init; }       // Control=Effort 时必填
    public int? BudgetTokens { get; init; }                   // Control=Budget 时必填
}
```

规则：

- `Control=Effort` 且 `Effort=null` → 构造期校验失败（消灭隐式默认）。
- `Control=Budget` 且 `BudgetTokens=null` → 同上。
- 旧 `Enabled` bool 废弃，迁移映射：`Enabled=false` → `Off`；`Enabled=true` → 按已填字段映射 `Effort`/`Budget`，两者皆空 → `ProviderDefault`。

### 5.2 档位 → 预算映射表

预算型模型（Anthropic manual、Gemini）由适配器内静态映射表翻译档位：

**Anthropic（`budget_tokens`，最小 1024，计入 max_tokens）**

| 档位 | 预算 | 依据 |
|------|------|------|
| None | 不传 thinking | 显式关闭 |
| Minimal | 1024 | API 最小值 |
| Low | 4096 | 简单任务区间 |
| Medium | 10000 | 官方建议中值，兼容现有代码默认值 |
| High | 16000 | 官方复杂任务起点 |
| XHigh | 32000 | 高预算，边际递减区间 |

**Gemini（`thinkingBudget`，0–24576）**

| 档位 | 预算 | 依据 |
|------|------|------|
| None | 0 | API 原生关闭语义 |
| Minimal | 1024 | 对齐 Anthropic 最小档 |
| Low | 4096 | |
| Medium | 8192 | |
| High | 16384 | |
| XHigh | 24576 | 上限 |

**clamp 规则**：映射结果超出模型边界时压到边界（低于下限抬到下限、高于上限压到上限），档位永远合法，用户无需感知各家边界。

**位置**：初版为适配器内 `private static` 表；M2 下沉到 Model 元数据并允许配置覆盖（如自定义 "High=24000"）。

### 5.3 ProviderOptions 逃生舱

需要精确控制成本的用户（如"跑批任务最多想 3000 token"）直接在 `AnthropicOptions` / Gemini options 传原生预算，绕过档位。统一层不感知。

### 5.4 适配器行为矩阵

| Control | OpenAI CC | OpenAI Responses | Anthropic | Gemini |
|---------|-----------|------------------|-----------|--------|
| Off | 不传 `reasoning_effort` | 不传 `reasoning` | 不传 `thinking` | `thinkingBudget: 0` |
| ProviderDefault | 不传 | 不传 | 不传 | 不传 |
| Effort | `reasoning_effort: {档位}`（模型不支持时降级到最近档） | `reasoning.effort` | 查映射表 → `thinking.budget_tokens` | 查映射表 → `thinkingBudget` |
| Budget | 不适用（400 拒绝） | 不适用 | `thinking.budget_tokens` 直通 | `thinkingBudget` 直通 |

### 5.5 `Off` 的实际语义与兼容端点

`Off` 是调用方的明确意图，不能与 `ProviderDefault` 的“不传参数”混同。对于已知支持关闭的模型，适配器必须发送供应商的显式关闭字段：

| 模型 / 端点 | `Off` 映射 | 备注 |
|---|---|---|
| OpenAI Responses / Chat Completions 推理模型 | `reasoning.effort = "none"` | 仅对已知支持该档位的模型发送；普通模型不发送该字段。 |
| Anthropic | 不传 `thinking` | Anthropic 的默认行为是不启用 extended thinking。 |
| Gemini | `thinkingConfig.thinkingBudget = 0` | 不传 `thinkingConfig` 才是供应商默认行为。 |
| GLM 5.x OpenAI 兼容端点 | `thinking.type = "disabled"` | GLM 官方文档支持 `enabled` / `disabled`。 |
| Qwen OpenAI 兼容端点 | `enable_thinking = false` | `reasoning.effort = none` 是较新的优先接口；模型支持矩阵仍需下沉到元数据。 |
| 豆包 / DeepSeek / MiniMax M3 OpenAI 兼容端点 | `thinking.type = "disabled"` | 各模型版本是否支持须由能力元数据确认。 |
| MiniMax M2.x | 无可用映射 | M2.x 即使接收 disabled 仍会思考；当前不伪造关闭参数。 |

当前 `OpenAIAdapter` 仅根据规范模型名前缀处理兼容端点。自定义 endpoint ID（例如托管平台的 `ep-*`）无法从模型名可靠判断能力，M2 的模型能力矩阵必须取代此前缀判断，并将“已应用 / 不支持 / 降级”暴露给日志或事件。

## 6. 分阶段实施

| 阶段 | 内容 | 备注 |
|------|------|------|
| **M1** 语义模型重构 | `ReasoningConfig` v2、枚举扩展、各适配器迁移到新模型、消灭隐式默认 | 不改外部行为，只改表达 |
| **M2** 能力矩阵下沉 | Model 元数据携带 reasoning 能力（支持的档位集合/预算边界/默认值），替代适配器内前缀数组；`/model` 展示可用档位 | 映射表同步下沉 |
| **M3** 运行时控制面 | `AgentConfig.Reasoning` 默认值 → 每轮可覆盖接口（预留给未来动态调节）→ CLI `/thinking` 命令 | 档位为会话级，不做自动调节 |
| **M4** 计量与压缩联动 | `TokenUsage.ReasoningTokens` 拆分（`usage.thinking_tokens` / reasoning tokens / `thoughtsTokenCount`）进 Dashboard；MicroCompact 对 `ThinkingBlock` 优先降级 | |

Gemini thinking 接入（`thinkingBudget` + `includeThoughts`）安排在 M2–M3 之间。

## 7. 决策记录

| 日期 | 决策 | 理由 |
|------|------|------|
| 2026-08-27 | 档位为统一主轴，预算为实现细节 | 三家机制在向档位收敛（Anthropic adaptive 化）；用户心智模型是档位 |
| 2026-08-27 | 预算不删除，作为 Budget 模式 + ProviderOptions 逃生舱 | Anthropic 存量模型仅支持 budget；Gemini 仅有预算语义 |
| 2026-08-27 | 预算型模型用固定映射表承接档位 | Anthropic 官方锚点（最小 1024 / 复杂 16000+）足以定表 |
| 2026-08-27 | 默认值为 `ProviderDefault` 而非显式档位 | 成本敏感，不意外多花思考 token，也不关掉模型既有推理（对比 Codex 默认 medium 的取舍） |
| 2026-08-27 | 不做每轮自动调节，M3 只留接口 | Anthropic 改参数破坏 cache；业界无公开先例 |
| 2026-08-28 | `Off` 与 `ProviderDefault` 严格分离 | 不传参数在各供应商的默认行为不同，不能代表关闭。 |

## 8. 开放问题

1. Anthropic adaptive 模式（Opus 4.7+ `output_config.effort`）的接入时点——倾向 M2/M3，接入时仅为 `AnthropicAdapter` 增加一个分支（档位直传，无需映射表）。
2. OpenAI 档位降级策略（模型不支持 `xhigh` 时降 `high`）的提示路径——静默降级 or 日志/事件提示。
3. `ThinkingBlock` 在 MicroCompact 中的降级优先级与 Anthropic 回传连续性的冲突——压缩丢弃 thinking block 是否影响后续轮次推理质量，需实测。
