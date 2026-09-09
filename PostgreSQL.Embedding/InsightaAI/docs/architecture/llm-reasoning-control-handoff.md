# LLM 思考控制交接手册

> **当前实现修订（2026-09-08，优先于本文全部历史记录）**：第一阶段重构已完成。产品调用方只能表达 `LlmRequest.ReasoningPreference`：`default` / `fast` / `off` / `balance` / `deep`。CLI 使用打包的 `Assets/model-reasoning-capabilities.json`，或 `CliConfig.models.*.reasoning` 的完整部署覆盖，将偏好解析为原生 `ReasoningConfig`；Adapter 只验证并序列化解析结果，已不再依据模型名内置关闭能力。
>
> `LlmRequest.Reasoning` 是 Adapter 可读的**已解析内部状态**，下一步应移除其公开 setter，改由受控的解析入口写入；普通调用方不得直接构造 `Effort` / `Budget` / `OffMode` 绕过能力目录。`ProviderOptions` 是 `extra_body` 风格的非推理供应商扩展（例如 service tier、top-p），不是第二条 reasoning 通道；不得通过 `ProviderOptions.Custom` 传递 `thinking`、`reasoning_effort`、`budget_tokens` 等控制字段。
>
> 未知模型只有 `default`。用户或 Agent 显式请求其它档位必须失败，不能猜测协议或静默降级。仅标题与摘要等内部辅助请求可以在 `off` 无映射时回退到 `default`，以避免上下文压缩失效；它不代表用户偏好的降级。CLI 尚未提供 `/thinking` 或运行时切换界面。
>
> 验证状态：`InsightaAI.Agent.Cli.Tests` 7/7、`InsightaAI.LLM.Tests` 121/121 通过；`dotnet build InsightaAI.sln --no-restore` 成功。Anthropic `budget_tokens < max_tokens` 仍为 TODO #21 的未决 P1；GLM 在线验证继续因额度不足而搁置。

> **2026-09-08 收尾修订（优先于下文 8 月记录）**：此前“所有 o 系列可发送 none”“所有 Gemini 可发送 budget=0”及按 GLM/Qwen 等家族前缀推断 Off 的结论不成立。当前使用 `ReasoningOffPolicy` 精确模型表：GPT-5.1/5.2 基础模型（含表内快照）使用 none；Gemini 2.5 Flash/Flash-Lite 使用预算 0；表内 Claude Sonnet 使用显式 disabled。o3-mini/GPT-5 等标记 Unsupported，其余未核对版本为 Unknown，均不发送关闭字段。兼容端点必须由调用方在确认协议后设置 `ReasoningConfig.OffMode`，例如 `ReasoningConfig.Off() with { OffMode = ReasoningOffMode.ThinkingDisabled }`。这是请求级能力覆盖，尚未接入 CLI 模型配置。解析结果记录在 `HttpRequestMessage.Options[ReasoningOffPolicy.ResolutionKey]` 与当前 Activity 的 `insighta.reasoning.off_resolution`，不代表服务端实测成功。
>
> **下一步架构决策（2026-09-08）**：面向用户的固定偏好改为 `default`、`fast`、`off`、`balance`、`deep`；其中 `default` 表示省略控制字段，`balance` 是显式均衡偏好，二者不可混同。能力、产品档位到原生参数的映射应下沉到内置模型能力目录，并允许 `CliConfig.models` 对自定义模型进行覆盖；Adapter 只序列化已解析的原生设置。能力未知时仅允许 `default`，其它偏好必须显式提示不支持，不能静默降级或按模型名前缀猜测。
>
> Effort/Budget 基础保留，尚未保证逐模型兼容性。Anthropic 预算与 max_tokens 的 P1 保留 TODO #21，等待用户决策。GLM 额度未恢复，本轮以 SKIP_REAL_API=true 验证，不作在线通过声明。
>
> 协议依据：[GPT-5.1](https://developers.openai.com/api/docs/models/gpt-5.1)、[GPT-5.2](https://developers.openai.com/api/docs/models/gpt-5.2)、[Gemini thinking](https://ai.google.dev/gemini-api/docs/thinking)。能力默认表有意保持小范围，后续扩展前应核对具体 API 与模型版本。


> 最后更新：2026-09-08。下文为 8 月交接历史，遇到冲突以本文顶部修订为准。
> 本次收尾验证：SKIP_REAL_API=true，LLM 122 项、Agent 332 项通过；真实 API 测试按配置提前返回，测试框架显示通过不代表在线验证。完整能力矩阵、CLI 模型配置接线与 Anthropic 预算策略尚未完成。

## 1. 本轮目标与边界

本轮不是为 CLI 增加“切换思维等级”的用户界面，而是先建立并验证 LLM 层的统一意图模型，优先保证：**调用方明确传递 `Off` 时，只要目标模型已知支持关闭，就发送供应商要求的显式关闭参数。**

`ProviderDefault` 的含义是“不发送任何思考控制参数”，绝不能用它替代 `Off`。不同供应商的默认行为不相同。

当前没有 CLI `/thinking` 命令，也没有在运行时自动选择 effort；不要在未补齐能力矩阵前贸然添加这两项。

## 2. 已完成的实现

### 2.1 公共模型

`src/InsightaAI.LLM/Models/LlmRequest.cs` 已将旧的布尔开关演进为：

```csharp
ReasoningControl: Off | ProviderDefault | Effort | Budget
ReasoningEffortLevel: None | Minimal | Low | Medium | High | XHigh
```

- `ReasoningConfig.Off()`：明确关闭。
- `new ReasoningConfig()`：供应商默认行为。
- `WithEffort(...)` / `WithBudget(...)`：保留昨天已完成的能力基础，不应因本轮只做 Off 而回退。
- `Validate()`：`Effort` 必须有档位，`Budget` 必须是正数。

`ReasoningMode.ThinkingBudget` 已加入 `IProviderAdapter` 的能力声明，Gemini 报告该能力。

### 2.2 当前 Off 请求映射

| 适配器 / 已知模型 | 发送字段 | ProviderDefault |
| --- | --- | --- |
| OpenAI Chat Completions：`o1` / `o3` / `gpt-5` 前缀 | `reasoning_effort: "none"` | 省略字段 |
| OpenAI Responses：`o1` / `o3` / `o4` / `gpt-5` 前缀 | `reasoning: { effort: "none" }` | 省略字段 |
| Gemini | `generationConfig.thinkingConfig.thinkingBudget: 0` | 省略 `thinkingConfig` |
| GLM 5.x 兼容端点 | `thinking: { type: "disabled" }` | 省略字段 |
| Qwen 兼容端点 | `enable_thinking: false` | 省略字段 |
| 豆包、DeepSeek reasoning、MiniMax M3 兼容端点 | `thinking: { type: "disabled" }` | 省略字段 |
| 未识别模型、MiniMax M2.x | 不发送关闭字段 | 省略字段 |

OpenAI 兼容请求模型 `OpenAIRequest` 新增了 `thinking` 和 `enable_thinking` 两个可选序列化属性。

### 2.3 框架内实际使用点

`src/InsightaAI.Agent/Context/Summary/SummaryService.cs` 的两类辅助请求已显式使用 `ReasoningConfig.Off()`：

- 会话标题生成；
- 上下文摘要生成。

目的不是改变主 Agent 的模型策略，而是避免辅助任务消耗不必要的思考 token。

## 3. 关键未决问题

### 3.1 P1：Anthropic `budget_tokens` 与 `max_tokens`

`AnthropicAdapter` 当前默认 `max_tokens = 4096`，而 effort 映射为：Minimal=1024、Low=4096、Medium=10000、High=16000、XHigh=32000。Anthropic 要求普通 extended-thinking 请求的 `budget_tokens < max_tokens`，因此 Low 及以上在调用方未指定更大 `MaxTokens` 时会被 API 拒绝。

这项已记录为 [TODO #21](../TODO.md)。用户尚未决定策略，**本轮不要自行修复或静默扩大用户显式指定的 MaxTokens**。待决建议：

1. 未显式设置 `MaxTokens` 时，使用 `max(defaultMaxTokens, budget + 1024)`；
2. 显式 `MaxTokens <= budget` 时，抛出清晰异常；
3. 为该规则补序列化测试。

### 3.2 能力识别仍是临时前缀判断

`OpenAIAdapter` / `OpenAIResponseAdapter` 目前按模型 ID 前缀决定是否发送关闭字段。它无法识别部署名、Azure deployment、网关别名或 `ep-*` 一类托管端点。

后续应建立模型能力矩阵，至少包含：

- provider 与具体模型/版本；
- 可接受的关闭字段与 effort 集合；
- 默认是否思考；
- `Off` 的结果：已应用、明确不支持、未知而未发送；
- 兼容端点的 endpoint/provider 线索，避免仅依赖字符串前缀。

在此之前，未知模型保持不发参数是刻意的安全降级，不是遗漏。

### 3.3 缺少“应用结果”诊断

`ReasoningConfig.Off()` 目前只能构造正确请求，调用方还无法获知该请求是“已关闭”还是“模型未知而未发送”。能力矩阵落地时，应增加低基数的诊断或响应元数据；不得把模型名、提示词、token 等写成 Prometheus label。

### 3.4 真实 API 验证暂时受阻

新增 `GlmAnthropicTests.cs` 包含 GLM 5.3 的真实 Anthropic 兼容端点测试。用户当前没有 GLM-5.3 额度，不能运行这些真实用例；此前环境错误也不应被描述为产品网络故障。

额度恢复后，用真实凭据运行 GLM 定向测试，并检查：请求是否被接受、`Off` 是否确实没有 thinking 输出、流式消息是否仍完整。不要用假设替代这一验证。

## 4. 测试状态与命令

本轮离线序列化测试覆盖：配置校验、Anthropic 映射、OpenAI Chat / Responses 的 `Off` 与 `ProviderDefault` 区分、GLM/Qwen/豆包/DeepSeek/MiniMax M3 的请求字段、未知模型降级，以及 Gemini `thinkingBudget: 0`。

最后一次结果：**110/110 通过**（排除需要真实 GLM 额度的测试）。

```powershell
dotnet test tests\InsightaAI.LLM.Tests\InsightaAI.LLM.Tests.csproj --no-restore --filter "FullyQualifiedName!~GlmAnthropicTests" -v:q
```

恢复额度后的补充命令：

```powershell
dotnet test tests\InsightaAI.LLM.Tests\InsightaAI.LLM.Tests.csproj --no-restore --filter "FullyQualifiedName~GlmAnthropicTests" -v:q
```

工作区还混有本功能开始前的未提交改动；提交或清理前先检查 `git status --short`，不要误删无关文件或父目录下的子模块状态。

## 5. 相关文件

| 用途 | 文件 |
| --- | --- |
| 设计与阶段决策 | [llm-reasoning-control-design.md](llm-reasoning-control-design.md) |
| 供应商调研与来源 | [llm-thinking-control-survey.md](../references/llm-thinking-control-survey.md) |
| 待决 Anthropic 策略 | [TODO #21](../TODO.md) |
| 公共请求模型 | `src/InsightaAI.LLM/Models/LlmRequest.cs` |
| OpenAI Chat 兼容映射 | `src/InsightaAI.LLM.OpenAI/OpenAIAdapter.cs` |
| OpenAI Responses 映射 | `src/InsightaAI.LLM.OpenAI/OpenAIResponseAdapter.cs` |
| Gemini 映射 | `src/InsightaAI.LLM.Gemini/GeminiAdapter.cs` |
| Anthropic 待修复映射 | `src/InsightaAI.LLM.Anthropic/AnthropicAdapter.cs` |
| 本轮离线测试 | `tests/InsightaAI.LLM.Tests/ReasoningControlTests.cs` |
| GLM 真实集成测试 | `tests/InsightaAI.LLM.Tests/GlmAnthropicTests.cs` |

## 6. 推荐接手顺序

1. 在 GLM 额度恢复后完成真实 GLM 定向回归；
2. 与用户确定 TODO #21 的 Anthropic `max_tokens` 策略并实现；
3. 为模型能力矩阵设计独立、可扩展的契约，替代散落的模型名前缀；
4. 加入“应用/降级/不支持”的可观察诊断；
5. 最后才考虑 CLI 配置或运行中切换思考强度。

## 7. 资料结论速记

- Qwen 同时存在 `enable_thinking` 与较新的 `reasoning.effort`，优先级和模型支持范围不同；
- GLM 5.x 使用 `thinking.type`，effort 档位与统一层不完全相同；
- Gemini 的 `thinkingBudget: 0` 是显式关闭，省略才是默认；
- MiniMax M2.x 无法真正关闭思考，MiniMax M3 使用 disabled/adaptive；
- Anthropic 的 thinking block 在多轮 tool-use 中有连续性要求，后续不要仅为简化而随意丢弃；
- 各供应商的完整来源与版本注意事项见调研文档，不要依赖本手册替代原始资料。
